using Cassandra;
using EsportApi.Models;
using EsportApi.Models.DTOs;
using EsportApi.Services.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;

namespace EsportApi.Services
{
    public class ShopService : IShopService
    {
        private readonly IMongoClient _mongo;
        private readonly IConnectionMultiplexer _redis;
        private readonly Cassandra.ISession _cassandra;

        public ShopService(IMongoClient mongo, IConnectionMultiplexer redis, Cassandra.ISession cassandra)
        {
            _mongo = mongo;
            _redis = redis;
            _cassandra = cassandra;

            _cassandra.Execute(@"
            CREATE TABLE IF NOT EXISTS esports.purchase_logs (
                year_month text,
                purchased_at timestamp,
                user_id text,
                item_id text,
                item_name text,
                price int,
                PRIMARY KEY (year_month, purchased_at)
            ) WITH CLUSTERING ORDER BY (purchased_at DESC)");
        }

        public async Task<string> BuyItemAsync(string userId, string itemId)
        {
            var db = _redis.GetDatabase();
            var lockKey = $"lock:shop:{itemId}";
            var stockKey = $"item_stock:{itemId}";

            // 1. REDIS LOCK
            if (!await db.LockTakeAsync(lockKey, "shop_lock", TimeSpan.FromSeconds(15)))
                return "Sistem je preopterećen, pokušaj ponovo.";

            try
            {
                var dbMongo = _mongo.GetDatabase("EsportDb");
                var shopItems = dbMongo.GetCollection<ShopItem>("ShopItems");
                var users = dbMongo.GetCollection<UserProfile>("Users");

                var item = await shopItems.Find(i => i.Id == itemId).FirstOrDefaultAsync();
                if (item == null) return "Predmet ne postoji.";

                // --- NADOGRADNJA 1: PROVERA DUPLIKATA (CASSANDRA) ---
                // Ne dozvoljavamo istom igraču da kupi dva ista Limited itema
                if (item.IsLimited)
                {
                    // Koristimo tvoj InventoryService da vidimo da li vec ima taj itemID
                    var alreadyOwned = await HasItemInCassandra(userId, itemId);
                    if (alreadyOwned) return "Već poseduješ ovaj limitirani predmet!";
                }

                // 2. REDIS: PROVERA ZALIHA
                if (item.IsLimited)
                {
                    var currentStock = await db.StringGetAsync(stockKey);
                    if (currentStock.IsNull)
                    {
                        await db.StringSetAsync(stockKey, item.CurrentStock); // Inicijalizuj iz Monga ako ga nema u Redisu
                        currentStock = item.CurrentStock;
                    }

                    if (int.Parse(currentStock!) <= 0)
                        return "RASPRODATO!";
                }

                // 3. MONGODB: TRANSAKCIJA
                using var session = await _mongo.StartSessionAsync();
                session.StartTransaction();
                try
                {
                    var userFilter = Builders<UserProfile>.Filter.Eq(u => u.Id, userId);
                    var user = await users.Find(session, userFilter).FirstOrDefaultAsync();

                    if (user == null || user.Coins < item.Price)
                    {
                        await session.AbortTransactionAsync();
                        return "Nemaš dovoljno novčića.";
                    }

                    // Skidamo pare korisniku
                    await users.UpdateOneAsync(session, userFilter, Builders<UserProfile>.Update.Inc(u => u.Coins, -item.Price));

                    // --- NADOGRADNJA 2: AŽURIRANJE ZALIHA U MONGODB-u ---
                    if (item.IsLimited)
                    {
                        await shopItems.UpdateOneAsync(session,
                            Builders<ShopItem>.Filter.Eq(i => i.Id, itemId),
                            Builders<ShopItem>.Update.Inc(i => i.CurrentStock, -1));
                    }

                    await session.CommitTransactionAsync();
                }
                catch
                {
                    await session.AbortTransactionAsync();
                    return "Greška u transakciji.";
                }

                // 4. REDIS: Smanjenje zaliha u memoriji
                if (item.IsLimited) await db.StringDecrementAsync(stockKey);

                // 5. CASSANDRA: Upis (Inventar + Log)
                var invQuery = "INSERT INTO esports.inventory (user_id, item_id, item_name, purchased_at) VALUES (?, ?, ?, toTimestamp(now()))";
                await _cassandra.ExecuteAsync((await _cassandra.PrepareAsync(invQuery)).Bind(userId, itemId, item.Name));

                var logQuery = "INSERT INTO esports.purchase_logs (year_month, purchased_at, user_id, item_id, item_name, price) VALUES (?, toTimestamp(now()), ?, ?, ?, ?)";
                await _cassandra.ExecuteAsync((await _cassandra.PrepareAsync(logQuery)).Bind(DateTime.UtcNow.ToString("yyyy-MM"), userId, itemId, item.Name, item.Price));

                return "USPEŠNO! Predmet je u tvom inventaru.";
            }
            catch (Exception ex) { return $"Greska: {ex.Message}"; }
            finally { await db.LockReleaseAsync(lockKey, "shop_lock"); }
        }

        // Pomoćna metoda za proveru u Cassandri
        private async Task<bool> HasItemInCassandra(string userId, string itemId)
        {
            var query = "SELECT item_id FROM esports.inventory WHERE user_id = ? AND item_id = ? ALLOW FILTERING";
            var prepared = await _cassandra.PrepareAsync(query);
            var result = await _cassandra.ExecuteAsync(prepared.Bind(userId, itemId));
            return result.FirstOrDefault() != null;
        }

        public async Task<MonthlyReportDto> GetMonthlyRevenueReportAsync(string yearMonth)
        {
            var query = "SELECT item_name, price FROM esports.purchase_logs WHERE year_month = ?";
            var prepared = await _cassandra.PrepareAsync(query);
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(yearMonth));

            int totalRevenue = 0;
            var itemCounts = new Dictionary<string, int>();

            foreach (var row in rows)
            {
                int price = row.GetValue<int>("price");
                string itemName = row.GetValue<string>("item_name");

                // 1. Sabiramo ukupnu zaradu
                totalRevenue += price;

                // 2. Brojimo prodaju svakog artikla
                if (itemCounts.ContainsKey(itemName))
                    itemCounts[itemName]++;
                else
                    itemCounts[itemName] = 1;
            }

            // Pronalazimo artikal sa najvećim brojem prodaja
            var bestSelling = itemCounts.OrderByDescending(x => x.Value).FirstOrDefault();

            return new MonthlyReportDto
            {
                Month = yearMonth,
                TotalRevenue = totalRevenue,
                BestSellingItem = bestSelling.Key ?? "Nema prodaje",
                SalesCount = bestSelling.Value
            };
        }
        public async Task<int> GetMonthlyRevenueAsync(string yearMonth)
        {
            var query = "SELECT price FROM esports.purchase_logs WHERE year_month = ?";
            var prepared = await _cassandra.PrepareAsync(query);
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(yearMonth));
            return rows.Sum(r => r.GetValue<int>("price"));
        }

        public async Task AddCoinsAsync(string userId, int amount)
        {
            var users = _mongo.GetDatabase("EsportDb").GetCollection<UserProfile>("Users");
            await users.UpdateOneAsync(u => u.Id == userId, Builders<UserProfile>.Update.Inc(u => u.Coins, amount));
        }

        public async Task<List<ShopItem>> GetAllItemsAsync()
        {
            return await _mongo.GetDatabase("EsportDb").GetCollection<ShopItem>("ShopItems").Find(_ => true).ToListAsync();
        }
    }
}
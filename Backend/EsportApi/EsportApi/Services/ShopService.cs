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
        }

        public async Task<string> BuyItemAsync(string userId, string itemId)
        {
            var db = _redis.GetDatabase();
            var lockKey = $"lock:shop:{itemId}";

            if (!await db.LockTakeAsync(lockKey, "shop_lock", TimeSpan.FromSeconds(10)))
                return "Predmet se trenutno kupuje, pokušaj za trenutak.";

            try
            {
                using var session = await _mongo.StartSessionAsync();
                session.StartTransaction();

                var dbMongo = _mongo.GetDatabase("EsportDb");
                var shopItems = dbMongo.GetCollection<ShopItem>("ShopItems");
                var users = dbMongo.GetCollection<UserProfile>("Users");

                var item = await shopItems.Find(session, i => i.Id == itemId).FirstOrDefaultAsync();
                if (item == null) { await session.AbortTransactionAsync(); return "Predmet ne postoji."; }

                var userFilter = Builders<UserProfile>.Filter.Eq(u => u.Id, userId);
                var user = await users.Find(session, userFilter).FirstOrDefaultAsync();

                if (user == null || user.Coins < item.Price)
                {
                    await session.AbortTransactionAsync();
                    return $"Nedovoljno novčića ili igrač ne postoji.";
                }

                var update = Builders<UserProfile>.Update.Inc(u => u.Coins, -item.Price);
                await users.UpdateOneAsync(session, userFilter, update);

                await session.CommitTransactionAsync();

                // --- CASSANDRA: INVENTAR (Za korisnika) ---
                var invQuery = "INSERT INTO esports.inventory (user_id, item_id, item_name, purchased_at) VALUES (?, ?, ?, toTimestamp(now()))";
                var stInv = await _cassandra.PrepareAsync(invQuery);
                await _cassandra.ExecuteAsync(stInv.Bind(userId, itemId, item.Name));

                // --- CASSANDRA: FINANCIAL LOG (Za biznis izveštaje) ---
                var yearMonth = DateTime.UtcNow.ToString("yyyy-MM");
                var logQuery = "INSERT INTO esports.purchase_logs (year_month, purchased_at, user_id, item_id, item_name, price) VALUES (?, toTimestamp(now()), ?, ?, ?, ?)";
                var stLog = await _cassandra.PrepareAsync(logQuery);
                await _cassandra.ExecuteAsync(stLog.Bind(yearMonth, userId, itemId, item.Name, item.Price));

                return "Uspešna kupovina!";
            }
            catch (Exception ex) { return $"Greska: {ex.Message}"; }
            finally { await db.LockReleaseAsync(lockKey, "shop_lock"); }
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
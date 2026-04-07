using Cassandra;
using EsportApi.Models;
using EsportApi.Models.DTOs;
using EsportApi.Services.Interfaces;
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
            var stockKey = $"item_stock:{itemId}";

            if (!await db.LockTakeAsync(lockKey, "shop_lock", TimeSpan.FromSeconds(15)))
            {
                return "Sistem je preopterećen, pokušaj ponovo.";
            }

            try
            {
                var dbMongo = _mongo.GetDatabase("EsportDb");
                var shopItems = dbMongo.GetCollection<ShopItem>("ShopItems");
                var users = dbMongo.GetCollection<UserProfile>("Users");

                var item = await shopItems.Find(i => i.Id == itemId).FirstOrDefaultAsync();
                if (item == null)
                {
                    return "Predmet ne postoji.";
                }

                if (item.IsLimited)
                {
                    var alreadyOwned = await HasItemInCassandra(userId, itemId);
                    if (alreadyOwned)
                    {
                        return "Već poseduješ ovaj limitirani predmet!";
                    }
                }

                if (item.IsLimited)
                {
                    var currentStock = await db.StringGetAsync(stockKey);
                    if (currentStock.IsNull)
                    {
                        await db.StringSetAsync(stockKey, item.CurrentStock);
                        currentStock = item.CurrentStock;
                    }

                    if (int.Parse(currentStock!) <= 0)
                    {
                        return "RASPRODATO!";
                    }
                }

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

                    await users.UpdateOneAsync(
                        session,
                        userFilter,
                        Builders<UserProfile>.Update.Inc(u => u.Coins, -item.Price));

                    if (item.IsLimited)
                    {
                        await shopItems.UpdateOneAsync(
                            session,
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

                if (item.IsLimited)
                {
                    await db.StringDecrementAsync(stockKey);
                }

                var purchasedAt = DateTime.UtcNow;
                var monthKey = purchasedAt.ToString("yyyy-MM");
                var purchaseId = Guid.NewGuid();

                var inventoryByUserQuery =
                    "INSERT INTO esports.inventory_by_user (user_id, purchased_at, item_id, item_name, purchase_price) VALUES (?, ?, ?, ?, ?)";
                await _cassandra.ExecuteAsync(
                    (await _cassandra.PrepareAsync(inventoryByUserQuery)).Bind(userId, purchasedAt, itemId, item.Name, item.Price));

                var inventoryLookupQuery =
                    "INSERT INTO esports.inventory_items_by_user (user_id, item_id, item_name, purchased_at, purchase_price) VALUES (?, ?, ?, ?, ?)";
                await _cassandra.ExecuteAsync(
                    (await _cassandra.PrepareAsync(inventoryLookupQuery)).Bind(userId, itemId, item.Name, purchasedAt, item.Price));

                var logQuery =
                    "INSERT INTO esports.purchase_logs_by_month (year_month, purchased_at, purchase_id, user_id, item_id, item_name, price) VALUES (?, ?, ?, ?, ?, ?, ?)";
                await _cassandra.ExecuteAsync(
                    (await _cassandra.PrepareAsync(logQuery)).Bind(monthKey, purchasedAt, purchaseId, userId, itemId, item.Name, item.Price));

                return "USPEŠNO! Predmet je u tvom inventaru.";
            }
            catch (Exception ex)
            {
                return $"Greska: {ex.Message}";
            }
            finally
            {
                await db.LockReleaseAsync(lockKey, "shop_lock");
            }
        }

        public async Task<string> SellItemAsync(string userId, string itemId, DateTime purchasedAt)
        {
            var dbMongo = _mongo.GetDatabase("EsportDb");
            var users = dbMongo.GetCollection<UserProfile>("Users");
            var shopItems = dbMongo.GetCollection<ShopItem>("ShopItems");

            var normalizedPurchasedAt = DateTime.SpecifyKind(purchasedAt, DateTimeKind.Utc);
            var inventoryRow = await GetInventoryRowAsync(userId, itemId, normalizedPurchasedAt);
            if (inventoryRow == null)
            {
                return "Predmet nije pronađen u inventaru.";
            }

            var purchasePrice = inventoryRow.PurchasePrice;
            if (purchasePrice <= 0)
            {
                var fallbackItem = await shopItems.Find(item => item.Id == itemId).FirstOrDefaultAsync();
                purchasePrice = fallbackItem?.Price ?? 0;
            }

            if (purchasePrice <= 0)
            {
                return "Cena predmeta nije dostupna za prodaju.";
            }

            var resalePrice = (int)Math.Floor(purchasePrice * 0.9m);
            if (resalePrice <= 0)
            {
                return "Predmet nema dovoljnu vrednost za prodaju.";
            }

            await users.UpdateOneAsync(
                profile => profile.Id == userId,
                Builders<UserProfile>.Update.Inc(profile => profile.Coins, resalePrice));

            if (inventoryRow.Source == InventorySource.Current)
            {
                var deleteCurrent = await _cassandra.PrepareAsync(
                    "DELETE FROM esports.inventory_by_user WHERE user_id = ? AND purchased_at = ? AND item_id = ?");
                await _cassandra.ExecuteAsync(deleteCurrent.Bind(userId, normalizedPurchasedAt, itemId));
            }
            else
            {
                var deleteLegacy = await _cassandra.PrepareAsync(
                    "DELETE FROM esports.inventory WHERE user_id = ? AND purchased_at = ? AND item_id = ?");
                await _cassandra.ExecuteAsync(deleteLegacy.Bind(userId, normalizedPurchasedAt, itemId));
            }

            var hasMoreCurrent = await UserStillOwnsItemAsync(
                "SELECT item_id FROM esports.inventory_by_user WHERE user_id = ?",
                userId,
                itemId);
            var hasMoreLegacy = await UserStillOwnsItemAsync(
                "SELECT item_id FROM esports.inventory WHERE user_id = ?",
                userId,
                itemId);

            if (!hasMoreCurrent && !hasMoreLegacy)
            {
                var deleteLookup = await _cassandra.PrepareAsync(
                    "DELETE FROM esports.inventory_items_by_user WHERE user_id = ? AND item_id = ?");
                await _cassandra.ExecuteAsync(deleteLookup.Bind(userId, itemId));
            }

            return $"USPEŠNO! Predmet je prodat za {resalePrice} coins.";
        }

        public async Task<MonthlyReportDto> GetMonthlyRevenueReportAsync(string yearMonth)
        {
            var rows = await ReadPurchaseRowsAsync(yearMonth, "SELECT item_name, price FROM esports.purchase_logs_by_month WHERE year_month = ?");

            int totalRevenue = 0;
            var itemCounts = new Dictionary<string, int>();

            foreach (var row in rows)
            {
                int price = row.GetValue<int>("price");
                string itemName = row.GetValue<string>("item_name");

                totalRevenue += price;

                if (itemCounts.ContainsKey(itemName))
                {
                    itemCounts[itemName]++;
                }
                else
                {
                    itemCounts[itemName] = 1;
                }
            }

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
            var rows = await ReadPurchaseRowsAsync(yearMonth, "SELECT price FROM esports.purchase_logs_by_month WHERE year_month = ?");
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

        private async Task<bool> HasItemInCassandra(string userId, string itemId)
        {
            var query = "SELECT item_id FROM esports.inventory_items_by_user WHERE user_id = ? AND item_id = ?";
            var prepared = await _cassandra.PrepareAsync(query);
            var result = await _cassandra.ExecuteAsync(prepared.Bind(userId, itemId));
            if (result.FirstOrDefault() != null)
            {
                return true;
            }

            var inventoryByUserPrepared = await _cassandra.PrepareAsync(
                "SELECT item_id FROM esports.inventory_by_user WHERE user_id = ?");
            var inventoryByUserRows = await _cassandra.ExecuteAsync(inventoryByUserPrepared.Bind(userId));
            if (inventoryByUserRows.Any(row => row.GetValue<string>("item_id") == itemId))
            {
                return true;
            }

            var legacyQuery = "SELECT item_id FROM esports.inventory WHERE user_id = ?";
            var legacyPrepared = await _cassandra.PrepareAsync(legacyQuery);
            var legacyRows = await _cassandra.ExecuteAsync(legacyPrepared.Bind(userId));
            return legacyRows.Any(row => row.GetValue<string>("item_id") == itemId);
        }

        private async Task<InventoryRow?> GetInventoryRowAsync(string userId, string itemId, DateTime purchasedAt)
        {
            var currentPrepared = await _cassandra.PrepareAsync(
                "SELECT item_name, purchase_price FROM esports.inventory_by_user WHERE user_id = ? AND purchased_at = ? AND item_id = ?");
            var currentRows = await _cassandra.ExecuteAsync(currentPrepared.Bind(userId, purchasedAt, itemId));
            var currentRow = currentRows.FirstOrDefault();

            if (currentRow != null)
            {
                return new InventoryRow
                {
                    ItemName = currentRow.GetValue<string>("item_name"),
                    PurchasePrice = currentRow.IsNull("purchase_price") ? 0 : currentRow.GetValue<int>("purchase_price"),
                    Source = InventorySource.Current
                };
            }

            var legacyPrepared = await _cassandra.PrepareAsync(
                "SELECT item_name FROM esports.inventory WHERE user_id = ? AND purchased_at = ? AND item_id = ?");
            var legacyRows = await _cassandra.ExecuteAsync(legacyPrepared.Bind(userId, purchasedAt, itemId));
            var legacyRow = legacyRows.FirstOrDefault();

            if (legacyRow == null)
            {
                return null;
            }

            return new InventoryRow
            {
                ItemName = legacyRow.GetValue<string>("item_name"),
                PurchasePrice = 0,
                Source = InventorySource.Legacy
            };
        }

        private async Task<bool> UserStillOwnsItemAsync(string query, string userId, string itemId)
        {
            var prepared = await _cassandra.PrepareAsync(query);
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(userId));
            return rows.Any(row => row.GetValue<string>("item_id") == itemId);
        }

        private async Task<RowSet> ReadPurchaseRowsAsync(string yearMonth, string primaryQuery)
        {
            var prepared = await _cassandra.PrepareAsync(primaryQuery);
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(yearMonth));

            if (rows.Any())
            {
                return rows;
            }

            var legacyPrepared = await _cassandra.PrepareAsync("SELECT item_name, price FROM esports.purchase_logs WHERE year_month = ?");
            return await _cassandra.ExecuteAsync(legacyPrepared.Bind(yearMonth));
        }

        private sealed class InventoryRow
        {
            public required string ItemName { get; set; }
            public int PurchasePrice { get; set; }
            public InventorySource Source { get; set; }
        }

        private enum InventorySource
        {
            Current,
            Legacy
        }
    }
}

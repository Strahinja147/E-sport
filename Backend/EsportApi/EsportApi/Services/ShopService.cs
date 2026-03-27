using Cassandra;
using EsportApi.Models;
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

            // 1. REDIS LOCK (Sprečava Race Condition)
            if (!await db.LockTakeAsync(lockKey, "shop_lock", TimeSpan.FromSeconds(10)))
                return "Predmet se trenutno kupuje, pokušaj za trenutak.";

            try
            {
                // 2. MONGO (Transakcija)
                using var session = await _mongo.StartSessionAsync();
                session.StartTransaction();

                var dbMongo = _mongo.GetDatabase("EsportDb");
                var shopItems = dbMongo.GetCollection<ShopItem>("ShopItems");
                var users = dbMongo.GetCollection<UserProfile>("Users");

                // Pronadji artikal (itemId je string koji odgovara ObjectId-ju u bazi)
                var item = await shopItems.Find(session, i => i.Id == itemId).FirstOrDefaultAsync();
                if (item == null)
                {
                    await session.AbortTransactionAsync();
                    return "Predmet ne postoji.";
                }

                // Pronadji korisnika (filter po string ID-ju)
                var userFilter = Builders<UserProfile>.Filter.Eq(u => u.Id, userId);
                var user = await users.Find(session, userFilter).FirstOrDefaultAsync();

                if (user == null || user.Coins < item.Price)
                {
                    await session.AbortTransactionAsync();
                    return $"Nedovoljno novčića ili igrač ne postoji. (Ima: {user?.Coins ?? 0}, Cena: {item.Price})";
                }

                // Skini pare (UpdateOne sa session-om i Filterom)
                var update = Builders<UserProfile>.Update.Inc(u => u.Coins, -item.Price);
                var result = await users.UpdateOneAsync(session, userFilter, update);

                if (result.ModifiedCount == 0)
                {
                    await session.AbortTransactionAsync();
                    return "Greška pri ažuriranju novčića u bazi.";
                }

                await session.CommitTransactionAsync();

                // 3. CASSANDRA (Zapisivanje inventara)
                // Napomena: itemId je string (iz Mongo), konvertujemo u Guid za Cassandru

                var insertQuery = "INSERT INTO esports.inventory (user_id, item_id, item_name, purchased_at) VALUES (?, ?, ?, ?)";
                var statement = await _cassandra.PrepareAsync(insertQuery);
                await _cassandra.ExecuteAsync(statement.Bind(userId, itemId, item.Name, DateTime.UtcNow));

                return "Uspešna kupovina!";
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

        public async Task AddCoinsAsync(string userId, int amount)
        {
            var users = _mongo.GetDatabase("EsportDb").GetCollection<UserProfile>("Users");
            var update = Builders<UserProfile>.Update.Inc(u => u.Coins, amount);
            // Koristimo direktan filter da budemo sigurni
            var filter = Builders<UserProfile>.Filter.Eq(u => u.Id, userId);
            await users.UpdateOneAsync(filter, update);
        }
        public async Task<List<ShopItem>> GetAllItemsAsync()
        {
            var shopItems = _mongo.GetDatabase("EsportDb").GetCollection<ShopItem>("ShopItems");

            // Asinhrono izvlačenje svih stavki iz kolekcije
            return await shopItems.Find(_ => true).ToListAsync();
        }
    }
}
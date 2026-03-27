using StackExchange.Redis;
using MongoDB.Driver;
using EsportApi.Models;

namespace EsportApi.Services
{
    public class MatchmakingService : IMatchmakingService
    {
        private readonly IDatabase _redisDb;
        private readonly IMongoCollection<UserProfile> _userCollection;

        public MatchmakingService(IConnectionMultiplexer redis, IMongoClient mongoClient)
        {
            // Povezivanje na Redis
            _redisDb = redis.GetDatabase();

            // Povezivanje na MongoDB kolekciju
            var mongoDb = mongoClient.GetDatabase("EsportDb");
            _userCollection = mongoDb.GetCollection<UserProfile>("UserProfiles");
        }

        // --- MATCHMAKING (Redis Lists) ---
        public async Task AddToQueue(string userId)
        {
            // LPUSH: Dodajemo igraca u red (Redis lista)
            await _redisDb.ListRightPushAsync("matchmaking_queue", userId);
        }

        public async Task<string?> TryMatch()
        {
            // 1. Uzmi prvog igraca iz reda (Redis lista)
            var p1Id = await _redisDb.ListLeftPopAsync("matchmaking_queue");
            if (!p1Id.HasValue) return null;

            // 2. Procitaj njegov EloRating iz MONGODB-a
            var p1Profile = await _userCollection.Find(u => u.Id == p1Id.ToString()).FirstOrDefaultAsync();
            int p1Elo = p1Profile?.EloRating ?? 1000;

            // 3. Pogledaj ostale ljude u redu (uzmi prvih 10 radi brzine)
            var potentialOpponents = await _redisDb.ListRangeAsync("matchmaking_queue", 0, 9);

            foreach (var p2Value in potentialOpponents)
            {
                string p2Id = p2Value.ToString();

                // 4. Procitaj rejting potencijalnog protivnika iz MONGODB-a
                var p2Profile = await _userCollection.Find(u => u.Id == p2Id).FirstOrDefaultAsync();
                int p2Elo = p2Profile?.EloRating ?? 1000;

                // 5. FER UPARIVANJE: Da li je razlika u rejtingu manja od 200?
                if (Math.Abs(p1Elo - p2Elo) <= 200)
                {
                    // Nasli smo fer protivnika! Izbaci ga iz reda u Redisu
                    await _redisDb.ListRemoveAsync("matchmaking_queue", p2Id);

                    string matchId = Guid.NewGuid().ToString();
                    return $"FER MEC KREIRAN! ID: {matchId} | {p1Profile.Username} ({p1Elo}) vs {p2Profile.Username} ({p2Elo})";
                }
            }

            // Ako nismo nasli nikog slicnog, vrati prvog igraca nazad u red da ceka dalje
            await _redisDb.ListLeftPushAsync("matchmaking_queue", p1Id);
            return null; 
        }

        // --- LEADERBOARD (Redis Sorted Sets + Mongo) ---
        public async Task AddWin(string userId)
        {
            // ZINCRBY: Povecava skor za 1 u Sorted Set-u pod kljucem "leaderboard"
            await _redisDb.SortedSetIncrementAsync("leaderboard", userId, 1);
        }

        public async Task<List<LeaderboardEntry>> GetTopPlayers(int count)
        {
            // 1. Izvuci top N ID-jeva iz Redisa (Sorted Set)
            var topFromRedis = await _redisDb.SortedSetRangeByRankWithScoresAsync("leaderboard", 0, count - 1, Order.Descending);

            var result = new List<LeaderboardEntry>();

            foreach (var entry in topFromRedis)
            {
                string userId = entry.Element.ToString();

                // 2. Za svaki ID, "skokni" do MongoDB-a po Username
                var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();

                result.Add(new LeaderboardEntry
                {
                    UserId = userId,
                    Wins = (int)entry.Score,
                    Username = user?.Username ?? "Unknown Player"
                });
            }
            return result;
        }
    }
}
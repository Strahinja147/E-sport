using StackExchange.Redis;
using MongoDB.Driver;
using EsportApi.Models;
using EsportApi.Services.Interfaces;

namespace EsportApi.Services
{
    public class MatchmakingService : IMatchmakingService
    {
        private readonly IDatabase _redisDb;
        private readonly IMongoCollection<UserProfile> _userCollection;
        private readonly IGameService _gameService; // <-- DODATO

        public MatchmakingService(IConnectionMultiplexer redis, IMongoClient mongoClient, IGameService gameService)
        {
            // Povezivanje na Redis
            _redisDb = redis.GetDatabase();

            // Povezivanje na MongoDB kolekciju
            var mongoDb = mongoClient.GetDatabase("EsportDb");
            _userCollection = mongoDb.GetCollection<UserProfile>("Users");
            _gameService = gameService; // <-- DODATO
        }

        // --- MATCHMAKING (Redis Lists) ---
        public async Task AddToQueue(string userId)
        {
            // LPUSH: Dodajemo igraca u red (Redis lista)
            await _redisDb.ListRightPushAsync("matchmaking_queue", userId);
        }

        public async Task<MatchFoundDto?> TryMatch()
        {
            var p1Id = await _redisDb.ListLeftPopAsync("matchmaking_queue");
            if (!p1Id.HasValue) return null;

            var p1Profile = await _userCollection.Find(u => u.Id == p1Id.ToString()).FirstOrDefaultAsync();

            // 1. ODBRANA OD "DUHOVA": Ako Player 1 ne postoji u Mongu, 
            // ignorisemo ga (vec smo ga izbacili iz Redisa sa Pop) i prekidamo.
            if (p1Profile == null) return null;

            int p1Elo = p1Profile.EloRating;

            var potentialOpponents = await _redisDb.ListRangeAsync("matchmaking_queue", 0, 9);

            foreach (var p2Value in potentialOpponents)
            {
                string p2Id = p2Value.ToString();
                var p2Profile = await _userCollection.Find(u => u.Id == p2Id).FirstOrDefaultAsync();

                // 2. ODBRANA: Ako potencijalni protivnik ne postoji u Mongu,
                // brisemo ga iz Redis reda i prelazimo na sledeceg!
                if (p2Profile == null)
                {
                    await _redisDb.ListRemoveAsync("matchmaking_queue", p2Id);
                    continue;
                }

                int p2Elo = p2Profile.EloRating;

                if (Math.Abs(p1Elo - p2Elo) <= 200)
                {
                    await _redisDb.ListRemoveAsync("matchmaking_queue", p2Id);

                    string matchId = await _gameService.StartGameAsync(p1Id.ToString(), p2Id);

                    return new MatchFoundDto
                    {
                        MatchId = matchId,
                        Player1 = p1Profile.Username,
                        Player2 = p2Profile.Username
                    };
                }
            }

            await _redisDb.ListRightPushAsync("matchmaking_queue", p1Id);
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
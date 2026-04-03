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
        private readonly IGameService _gameService; 

        public MatchmakingService(IConnectionMultiplexer redis, IMongoClient mongoClient, IGameService gameService)
        {
            _redisDb = redis.GetDatabase();

            
            var mongoDb = mongoClient.GetDatabase("EsportDb");
            _userCollection = mongoDb.GetCollection<UserProfile>("Users");
            _gameService = gameService; 
        }

        public async Task AddToQueue(string userId)
        {
            
            await _redisDb.ListRightPushAsync("matchmaking_queue", userId);
        }

        public async Task<MatchFoundDto?> TryMatch()
        {
            var p1Id = await _redisDb.ListLeftPopAsync("matchmaking_queue");
            if (!p1Id.HasValue) return null;

            var p1Profile = await _userCollection.Find(u => u.Id == p1Id.ToString()).FirstOrDefaultAsync();

            if (p1Profile == null) return null;

            int p1Elo = p1Profile.EloRating;

            var potentialOpponents = await _redisDb.ListRangeAsync("matchmaking_queue", 0, 9);

            foreach (var p2Value in potentialOpponents)
            {
                string p2Id = p2Value.ToString();
                var p2Profile = await _userCollection.Find(u => u.Id == p2Id).FirstOrDefaultAsync();

                
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

        // NOVA METODA ZA LEADERBOARD (Rangira po ELO-u iz Monga i koristi Redis kao brzi keš)
        public async Task<List<LeaderboardEntry>> GetTopPlayers(int count = 10)
        {
            var topFromRedis = await _redisDb.SortedSetRangeByRankWithScoresAsync(
                "leaderboard_elo", 0, count - 1, Order.Descending);

            if (topFromRedis.Length == 0) return new List<LeaderboardEntry>();

            var ids = topFromRedis.Select(x => x.Element.ToString()).ToList();

            var users = await _userCollection.Find(u => ids.Contains(u.Id)).ToListAsync();

            var userDict = users.ToDictionary(u => u.Id, u => u);

            var result = new List<LeaderboardEntry>();
            int rank = 1;

            foreach (var entry in topFromRedis)
            {
                string userId = entry.Element.ToString();

                if (userDict.TryGetValue(userId, out var user))
                {
                    result.Add(new LeaderboardEntry
                    {
                        Rank = rank++,
                        UserId = userId,
                        Username = user.Username, // SAD IMAMO IME!
                        EloRating = (int)entry.Score,
                        Wins = user.Stats.Wins,
                        TournamentWins = user.Stats.TournamentWins
                    });
                }
            }

            return result;
        }
        public async Task UpdateLeaderboardCache(string userId, int newElo)
        {
            // Upisujemo u Sorted Set "leaderboard_elo"
            // Clan: UserId, Score: EloRating
            await _redisDb.SortedSetAddAsync("leaderboard_elo", userId, newElo);
        }

        public async Task SyncLeaderboardAsync()
        {
            // 1. Povuci sve korisnike iz MongoDB-a
            var allUsers = await _userCollection.Find(_ => true).ToListAsync();

            // 2. Napuni Redis Sorted Set
            foreach (var user in allUsers)
            {
                await _redisDb.SortedSetAddAsync("leaderboard_elo", user.Id, user.EloRating);
            }
        }

        public async Task<string> JoinTournamentQueueAsync(string userId)
        {
            var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (user == null) return "Korisnik ne postoji.";

            if (user.EloRating < 1200)
                return $"Nedovoljan Elo rejting za turnir. Tvoj Elo: {user.EloRating}, Minimum: 1200.";

            var queue = await _redisDb.ListRangeAsync("tournament_queue");
            if (queue.ToStringArray().Contains(userId))
                return "Vec si u redu za turnir.";

            await _redisDb.ListRightPushAsync("tournament_queue", userId);
            return "Uspešno prijavljen u red za turnir!";
        }

        public async Task<List<string>?> CheckTournamentQueueAsync(int requiredPlayers)
        {
            var length = await _redisDb.ListLengthAsync("tournament_queue");

            if (length >= requiredPlayers)
            {
                var players = new List<string>();
                
                for (int i = 0; i < requiredPlayers; i++)
                {
                    var p = await _redisDb.ListLeftPopAsync("tournament_queue");
                    players.Add(p.ToString());
                }
                return players; 
            }

            return null; 
        }
    }
}
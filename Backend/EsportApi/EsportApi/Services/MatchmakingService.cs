using EsportApi.Models;
using EsportApi.Services.Interfaces;
using MongoDB.Driver;
using StackExchange.Redis;
using System.Text.Json;

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
            if (await HasActiveMatchAsync(userId))
            {
                throw new InvalidOperationException("Vec imas aktivan mec.");
            }

            if (await IsUserInAnyQueueAsync(userId))
            {
                throw new InvalidOperationException("Vec si u redu za matchmaking ili turnir.");
            }

            await _redisDb.ListRemoveAsync("matchmaking_queue", userId, -1);
            await _redisDb.ListRightPushAsync("matchmaking_queue", userId);
        }

        public async Task<MatchFoundDto?> TryMatch()
        {
            var p1Id = await _redisDb.ListLeftPopAsync("matchmaking_queue");
            if (!p1Id.HasValue)
            {
                return null;
            }

            var p1Profile = await _userCollection.Find(u => u.Id == p1Id.ToString()).FirstOrDefaultAsync();
            if (p1Profile == null)
            {
                return null;
            }

            if (await HasActiveMatchAsync(p1Id.ToString()))
            {
                return null;
            }

            int p1Elo = p1Profile.EloRating;
            var potentialOpponents = await _redisDb.ListRangeAsync("matchmaking_queue", 0, 9);

            foreach (var p2Value in potentialOpponents)
            {
                string p2Id = p2Value.ToString();

                if (string.Equals(p1Id.ToString(), p2Id, StringComparison.Ordinal))
                {
                    await _redisDb.ListRemoveAsync("matchmaking_queue", p2Id, -1);
                    continue;
                }

                if (await HasActiveMatchAsync(p2Id))
                {
                    await _redisDb.ListRemoveAsync("matchmaking_queue", p2Id, -1);
                    continue;
                }

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
                    var match = new MatchFoundDto
                    {
                        MatchId = matchId,
                        Player1 = p1Profile.Username,
                        Player2 = p2Profile.Username,
                        Player1Id = p1Id.ToString(),
                        Player2Id = p2Id
                    };

                    await SaveAssignedMatchAsync(match);
                    return match;
                }
            }

            await _redisDb.ListRightPushAsync("matchmaking_queue", p1Id);
            return null;
        }

        public async Task<MatchFoundDto?> GetAssignedMatchAsync(string userId)
        {
            var matchId = await _redisDb.StringGetAsync($"user_active_match:{userId}");
            if (matchId.IsNullOrEmpty)
            {
                return null;
            }

            var gameState = await _redisDb.StringGetAsync($"match:{matchId}");
            if (gameState.IsNullOrEmpty)
            {
                await _redisDb.KeyDeleteAsync($"user_active_match:{userId}");
                return null;
            }

            var game = JsonSerializer.Deserialize<TicTacToeGame>(gameState!);
            if (game == null)
            {
                await _redisDb.KeyDeleteAsync($"user_active_match:{userId}");
                return null;
            }

            if (!string.Equals(game.Status, "InProgress", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(game.Player1Id))
                {
                    await _redisDb.KeyDeleteAsync($"user_active_match:{game.Player1Id}");
                }

                if (!string.IsNullOrWhiteSpace(game.Player2Id))
                {
                    await _redisDb.KeyDeleteAsync($"user_active_match:{game.Player2Id}");
                }

                return null;
            }

            var playerEntries = await _redisDb.HashGetAllAsync($"match_players:{matchId}");
            if (playerEntries.Length == 0)
            {
                await _redisDb.KeyDeleteAsync($"user_active_match:{userId}");
                return null;
            }

            var playerMap = playerEntries.ToDictionary(entry => entry.Name.ToString(), entry => entry.Value.ToString());
            if (!playerMap.TryGetValue("P1", out var player1Id) || !playerMap.TryGetValue("P2", out var player2Id))
            {
                return null;
            }

            var users = await _userCollection.Find(u => u.Id == player1Id || u.Id == player2Id).ToListAsync();
            var player1 = users.FirstOrDefault(u => u.Id == player1Id);
            var player2 = users.FirstOrDefault(u => u.Id == player2Id);

            if (player1 == null || player2 == null)
            {
                return null;
            }

            return new MatchFoundDto
            {
                MatchId = matchId.ToString(),
                Player1 = player1.Username,
                Player2 = player2.Username,
                Player1Id = player1Id,
                Player2Id = player2Id
            };
        }

        public async Task<List<LeaderboardEntry>> GetTopPlayers(int count = 10)
        {
            var topFromRedis = await _redisDb.SortedSetRangeByRankWithScoresAsync(
                "leaderboard_elo", 0, count - 1, Order.Descending);

            if (topFromRedis.Length == 0)
            {
                return new List<LeaderboardEntry>();
            }

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
                        Username = user.Username,
                        EloRating = (int)entry.Score,
                        Wins = user.Stats.Wins,
                        TournamentWins = user.Stats.TournamentWins
                    });
                }
            }

            return result;
        }

        public async Task<string> JoinTournamentQueueAsync(string userId)
        {
            var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (user == null)
            {
                return "Korisnik ne postoji.";
            }

            if (user.EloRating < 1200)
            {
                return $"Nedovoljan Elo rejting za turnir. Tvoj Elo: {user.EloRating}, Minimum: 1200.";
            }

            if (await HasActiveMatchAsync(userId))
            {
                return "Vec imas aktivan mec.";
            }

            var normalQueue = await _redisDb.ListRangeAsync("matchmaking_queue");
            if (normalQueue.ToStringArray().Contains(userId))
            {
                return "Vec si u obicnom matchmaking redu.";
            }

            var queue = await _redisDb.ListRangeAsync("tournament_queue");
            if (queue.ToStringArray().Contains(userId))
            {
                return "Vec si u redu za turnir.";
            }

            await _redisDb.ListRemoveAsync("tournament_queue", userId, -1);
            await _redisDb.ListRightPushAsync("tournament_queue", userId);
            return "Uspesno prijavljen u red za turnir!";
        }

        public async Task<List<string>?> CheckTournamentQueueAsync(int requiredPlayers)
        {
            var length = await _redisDb.ListLengthAsync("tournament_queue");

            if (length < requiredPlayers)
            {
                return null;
            }

            var players = new List<string>();

            for (int i = 0; i < requiredPlayers; i++)
            {
                var player = await _redisDb.ListLeftPopAsync("tournament_queue");
                players.Add(player.ToString());
            }

            return players;
        }

        private async Task SaveAssignedMatchAsync(MatchFoundDto match)
        {
            var ttl = TimeSpan.FromMinutes(30);
            await _redisDb.StringSetAsync($"user_active_match:{match.Player1Id}", match.MatchId, ttl);
            await _redisDb.StringSetAsync($"user_active_match:{match.Player2Id}", match.MatchId, ttl);
        }

        private async Task<bool> HasActiveMatchAsync(string userId)
        {
            var matchId = await _redisDb.StringGetAsync($"user_active_match:{userId}");
            if (matchId.IsNullOrEmpty)
            {
                return false;
            }

            var gameState = await _redisDb.StringGetAsync($"match:{matchId}");
            if (gameState.IsNullOrEmpty)
            {
                await _redisDb.KeyDeleteAsync($"user_active_match:{userId}");
                return false;
            }

            var game = JsonSerializer.Deserialize<TicTacToeGame>(gameState!);
            if (game == null || !string.Equals(game.Status, "InProgress", StringComparison.OrdinalIgnoreCase))
            {
                await _redisDb.KeyDeleteAsync($"user_active_match:{userId}");
                return false;
            }

            return true;
        }

        private async Task<bool> IsUserInAnyQueueAsync(string userId)
        {
            var normalQueue = await _redisDb.ListRangeAsync("matchmaking_queue");
            if (normalQueue.ToStringArray().Contains(userId))
            {
                return true;
            }

            var tournamentQueue = await _redisDb.ListRangeAsync("tournament_queue");
            return tournamentQueue.ToStringArray().Contains(userId);
        }
    }
}

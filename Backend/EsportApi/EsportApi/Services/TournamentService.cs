using EsportApi.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using StackExchange.Redis;
using System.Text.Json;
using EsportApi.Models.DTOs;
using EsportApi.Services.Interfaces;
using Cassandra;

namespace EsportApi.Services
{
    public class TournamentService : ITournamentService
    {
        private readonly IMongoClient _mongoClient;
        private readonly IMongoCollection<Tournament> _tournamentsCollection;
        private readonly IMongoCollection<Match> _matchesCollection;
        private readonly IMongoCollection<UserProfile> _usersCollection;
        private readonly IDatabase _redisDb;
        private readonly Cassandra.ISession _cassandra;
        private readonly IRedisRealtimePublisher _realtimePublisher;

        public TournamentService(
            IMongoClient mongoClient,
            IConnectionMultiplexer redis,
            Cassandra.ISession cassandra,
            IRedisRealtimePublisher realtimePublisher)
        {
            _mongoClient = mongoClient;
            var db = _mongoClient.GetDatabase("EsportDb");

            _tournamentsCollection = db.GetCollection<Tournament>("Tournaments");
            _matchesCollection = db.GetCollection<Match>("Matches");
            _usersCollection = db.GetCollection<UserProfile>("Users");
            _redisDb = redis.GetDatabase();
            _cassandra = cassandra;
            _realtimePublisher = realtimePublisher;
        }

        public async Task<Tournament> CreateTournament(string name)
        {
            var tournament = new Tournament
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = name,
                Rounds = new List<TournamentRound>()
            };

            await _tournamentsCollection.InsertOneAsync(tournament);
            return tournament;
        }

        public async Task<List<Tournament>> GetAllTournaments()
        {
            return await _tournamentsCollection.Find(_ => true).ToListAsync();
        }

        public async Task<bool> AdvanceWinner(string tournamentId, string matchId, string winnerId)
        {
            using var session = await _mongoClient.StartSessionAsync();
            session.StartTransaction();
            var matchesToInitialize = new List<Match>();
            try
            {
                var matchFilter = Builders<Match>.Filter.Eq(m => m.Id, matchId);
                var matchUpdate = Builders<Match>.Update.Set(m => m.WinnerId, winnerId).Set(m => m.Status, "Finished");
                await _matchesCollection.UpdateOneAsync(session, matchFilter, matchUpdate);

                var tourFilter = Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId);
                var tournament = await _tournamentsCollection.Find(session, tourFilter).FirstOrDefaultAsync();

                if (tournament == null) throw new Exception("Turnir ne postoji!");

                var currentRound = tournament.Rounds.FirstOrDefault(r => r.MatchIds.Contains(matchId));
                if (currentRound == null) throw new Exception("Meč ne pripada ovom turniru!");

                if (currentRound.MatchIds.Count == 1)
                {
                    Console.WriteLine($"\n*** KRAJ TURNIRA! APSOLUTNI POBEDNIK JE: {winnerId} ***\n");
                    tournament.Status = "Completed";

                    var winner = await _usersCollection.Find(session, u => u.Id == winnerId).FirstOrDefaultAsync();
                    if (winner != null)
                    {
                        int newElo = winner.EloRating + 500;
                        int newCoins = winner.Coins + 500;
                        int newTourWins = winner.Stats.TournamentWins + 1;
                        double newTourWinRate = winner.Stats.TournamentsPlayed > 0
                            ? Math.Round((double)newTourWins / winner.Stats.TournamentsPlayed, 2)
                            : 1.0;

                        var winnerUpdate = Builders<UserProfile>.Update
                            .Set(u => u.EloRating, newElo)
                            .Set(u => u.Coins, newCoins)
                            .Set(u => u.Stats.TournamentWins, newTourWins)
                            .Set(u => u.Stats.TournamentWinRate, newTourWinRate);

                        await _usersCollection.UpdateOneAsync(session, u => u.Id == winnerId, winnerUpdate);

                        await _redisDb.SortedSetAddAsync("leaderboard_elo", winnerId, newElo);

                        var progQuery = "INSERT INTO esports.player_progress_history_by_user (user_id, recorded_at, entry_id, elo, coins, change_reason) VALUES (?, toTimestamp(now()), ?, ?, ?, ?)";
                        var preparedProg = await _cassandra.PrepareAsync(progQuery);
                        await _cassandra.ExecuteAsync(preparedProg.Bind(winnerId, Guid.NewGuid(), newElo, newCoins, "Tournament Win"));
                    }

                    await _tournamentsCollection.ReplaceOneAsync(session, tourFilter, tournament);

                    await session.CommitTransactionAsync();
                    await _redisDb.KeyDeleteAsync($"tournament:{tournamentId}");
                    return true;
                }

                int nextRoundNumber = currentRound.RoundNumber + 1;
                var nextRound = tournament.Rounds.FirstOrDefault(r => r.RoundNumber == nextRoundNumber);

                bool tournamentUpdated = false;

                if (nextRound == null)
                {
                    var newMatch = new Match
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        Player1Id = winnerId,
                        Player2Id = "TBD",
                        Status = "Pending",
                        TournamentId = tournamentId // Bitno za tvoj Lazy Load!
                    };
                    await _matchesCollection.InsertOneAsync(session, newMatch);

                    tournament.Rounds.Add(new TournamentRound
                    {
                        RoundNumber = nextRoundNumber,
                        MatchIds = new List<string> { newMatch.Id }
                    });
                    tournamentUpdated = true;
                }
                else
                {
                    var nextRoundMatches = await _matchesCollection
                        .Find(session, Builders<Match>.Filter.In(m => m.Id, nextRound.MatchIds))
                        .ToListAsync();

                    var waitingMatch = nextRoundMatches.FirstOrDefault(m => m.Player2Id == "TBD");

                    if (waitingMatch != null)
                    {
                        var updateWaitingMatch = Builders<Match>.Update
                            .Set(m => m.Player2Id, winnerId)
                            .Set(m => m.Status, "InProgress");
                        await _matchesCollection.UpdateOneAsync(session, m => m.Id == waitingMatch.Id, updateWaitingMatch);
                        waitingMatch.Player2Id = winnerId;
                        waitingMatch.Status = "InProgress";
                        matchesToInitialize.Add(waitingMatch);
                    }
                    else
                    {
                        var newMatch = new Match
                        {
                            Id = ObjectId.GenerateNewId().ToString(),
                            Player1Id = winnerId,
                            Player2Id = "TBD",
                            Status = "Pending",
                            TournamentId = tournamentId
                        };
                        await _matchesCollection.InsertOneAsync(session, newMatch);
                        nextRound.MatchIds.Add(newMatch.Id);
                        tournamentUpdated = true;
                    }
                }

                if (tournamentUpdated)
                {
                    await _tournamentsCollection.ReplaceOneAsync(session, tourFilter, tournament);
                }

                await session.CommitTransactionAsync();
                await _redisDb.KeyDeleteAsync($"tournament:{tournamentId}");

                foreach (var nextMatch in matchesToInitialize)
                {
                    await InitializeTournamentMatchAsync(nextMatch);
                }

                return true;
            }
            catch (Exception ex)
            {
                await session.AbortTransactionAsync();
                Console.WriteLine($"Transakcija propala: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> GenerateBracket(string tournamentId, List<string> playerIds)
        {
            if (playerIds.Count < 2 || playerIds.Count % 2 != 0)
            {
                throw new Exception("Broj igrača mora biti paran (npr. 2, 4, 8)!");
            }

            var cleanedPlayerIds = playerIds.Select(id => id.Trim()).Distinct().ToList();

            if (cleanedPlayerIds.Count != playerIds.Count)
            {
                throw new Exception("Greška: Prosledio si duple ID-jeve u Swaggeru!");
            }

            var filter = Builders<UserProfile>.Filter.In(u => u.Id, cleanedPlayerIds);
            var existingUsers = await _usersCollection.Find(filter).ToListAsync();

            if (existingUsers.Count != cleanedPlayerIds.Count)
            {
                var pronadjeniIdjevi = existingUsers.Select(u => u.Id).ToList();
                var faleU_Bazi = cleanedPlayerIds.Except(pronadjeniIdjevi).ToList();

                throw new Exception($"Greška u bazi! Ovi ID-jevi ne postoje: {string.Join(", ", faleU_Bazi)}");
            }

            using var session = await _mongoClient.StartSessionAsync();
            session.StartTransaction();
            var matchesToInitialize = new List<Match>();
            try
            {
                var matchIdsForRound1 = new List<string>();

                for (int i = 0; i < playerIds.Count; i += 2)
                {
                    var newMatch = new Match
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        Player1Id = playerIds[i],
                        Player2Id = playerIds[i + 1],
                        Status = "InProgress",
                        TournamentId = tournamentId
                    };

                    await _matchesCollection.InsertOneAsync(session, newMatch);
                    matchIdsForRound1.Add(newMatch.Id);
                    matchesToInitialize.Add(newMatch);
                }

                var tourFilter = Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId);
                var tournament = await _tournamentsCollection.Find(session, tourFilter).FirstOrDefaultAsync();

                if (tournament != null)
                {
                    tournament.Rounds.Add(new TournamentRound
                    {
                        RoundNumber = 1,
                        MatchIds = matchIdsForRound1
                    });
                    tournament.Status = "On Going";

                    await _tournamentsCollection.ReplaceOneAsync(session, tourFilter, tournament);
                }

                foreach (var playerId in cleanedPlayerIds)
                {
                    var user = await _usersCollection.Find(session, u => u.Id == playerId).FirstOrDefaultAsync();
                    if (user != null)
                    {
                        int newPlayed = user.Stats.TournamentsPlayed + 1;
                        int currentWins = user.Stats.TournamentWins;

                        double newTourWinRate = Math.Round((double)currentWins / newPlayed, 2);

                        var updateStats = Builders<UserProfile>.Update
                            .Set(u => u.Stats.TournamentsPlayed, newPlayed)
                            .Set(u => u.Stats.TournamentWinRate, newTourWinRate);

                        await _usersCollection.UpdateOneAsync(session, u => u.Id == playerId, updateStats);
                    }
                }
                await session.CommitTransactionAsync();

                await _redisDb.KeyDeleteAsync($"tournament:{tournamentId}");

                foreach (var match in matchesToInitialize)
                {
                    await InitializeTournamentMatchAsync(match);
                }

                return true;
            }
            catch (Exception ex)
            {
                await session.AbortTransactionAsync();
                Console.WriteLine($"Greska pri pravljenju zreba: {ex.Message}");
                return false;
            }
        }

        public async Task<TournamentDetailsDto?> GetTournamentWithDetails(string tournamentId)
        {
            var tournament = await _tournamentsCollection.Find(t => t.Id == tournamentId).FirstOrDefaultAsync();
            if (tournament == null) return null;

            var allMatchIds = tournament.Rounds.SelectMany(r => r.MatchIds).ToList();
            var matches = await _matchesCollection.Find(m => allMatchIds.Contains(m.Id)).ToListAsync();

            var userIds = matches.Select(m => m.Player1Id)
                .Union(matches.Select(m => m.Player2Id))
                .Union(matches.Select(m => m.WinnerId))
                .Where(id => id != null && id != "TBD")
                .Distinct()
                .ToList();

            var users = await _usersCollection.Find(u => userIds.Contains(u.Id)).ToListAsync();

            var userDict = users.ToDictionary(u => u.Id, u => new PlayerDto { Id = u.Id, Username = u.Username });

            userDict["TBD"] = new PlayerDto { Id = "TBD", Username = "Čeka se protivnik..." };

            var result = new TournamentDetailsDto
            {
                Id = tournament.Id,
                Name = tournament.Name,
                Rounds = tournament.Rounds.Select(r => new TournamentRoundDto
                {
                    RoundNumber = r.RoundNumber,
                    Matches = matches.Where(m => r.MatchIds.Contains(m.Id)).Select(m => new MatchDetailsDto
                    {
                        Id = m.Id,
                        Status = m.Status,
                        Player1 = m.Player1Id != null && userDict.ContainsKey(m.Player1Id) ? userDict[m.Player1Id] : null,
                        Player2 = m.Player2Id != null && userDict.ContainsKey(m.Player2Id) ? userDict[m.Player2Id] : null,
                        Winner = m.WinnerId != null && userDict.ContainsKey(m.WinnerId) ? userDict[m.WinnerId] : null
                    }).ToList()
                }).ToList()
            };

            return result;
        }

        private async Task InitializeTournamentMatchAsync(Match match)
        {
            if (string.IsNullOrWhiteSpace(match.Player1Id) ||
                string.IsNullOrWhiteSpace(match.Player2Id) ||
                match.Player2Id == "TBD")
            {
                return;
            }

            var liveMatch = new TicTacToeGame
            {
                Id = match.Id,
                TournamentId = match.TournamentId,
                Board = "_________",
                CurrentTurn = "X",
                Status = "InProgress",
                Version = 1,
                Player1Id = match.Player1Id,
                Player2Id = match.Player2Id
            };

            var ttl = TimeSpan.FromMinutes(30);
            var now = DateTime.UtcNow;

            await _redisDb.HashSetAsync($"match_players:{match.Id}", new HashEntry[]
            {
                new HashEntry("P1", match.Player1Id),
                new HashEntry("P2", match.Player2Id),
                new HashEntry("LastMoveAt", now.Ticks.ToString())
            });

            await _redisDb.StringSetAsync($"match:{match.Id}", JsonSerializer.Serialize(liveMatch), ttl);
            await _redisDb.StringSetAsync($"user_active_match:{match.Player1Id}", match.Id, ttl);
            await _redisDb.StringSetAsync($"user_active_match:{match.Player2Id}", match.Id, ttl);

            var users = await _usersCollection.Find(u => u.Id == match.Player1Id || u.Id == match.Player2Id).ToListAsync();
            var player1 = users.FirstOrDefault(u => u.Id == match.Player1Id);
            var player2 = users.FirstOrDefault(u => u.Id == match.Player2Id);

            if (player1 != null && player2 != null)
            {
                await _realtimePublisher.PublishMatchFoundAsync(new MatchFoundDto
                {
                    MatchId = match.Id,
                    Player1 = player1.Username,
                    Player2 = player2.Username,
                    Player1Id = player1.Id,
                    Player2Id = player2.Id
                });
            }
        }
    }
}

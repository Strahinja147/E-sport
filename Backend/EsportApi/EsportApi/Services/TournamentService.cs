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
        private readonly Cassandra.ISession _cassandra; // DODATO
        private readonly IRedisRealtimePublisher _realtimePublisher;

        public TournamentService(
            IMongoClient mongoClient,
            IConnectionMultiplexer redis,
            Cassandra.ISession cassandra,
            IRedisRealtimePublisher realtimePublisher)
        {
            _mongoClient = mongoClient;
            var db = _mongoClient.GetDatabase("EsportDb");

            // Povezujemo se na dve kolekcije da bi radili transakciju nad obe
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

        // --- MAGIJA TRANSAKCIJE POČINJE OVDE ---
        public async Task<bool> AdvanceWinner(string tournamentId, string matchId, string winnerId)
        {
            using var session = await _mongoClient.StartSessionAsync();
            session.StartTransaction();
            var matchesToInitialize = new List<Match>();
            try
            {
                // 1. Zapiši ko je pobedio u trenutnom meču
                var matchFilter = Builders<Match>.Filter.Eq(m => m.Id, matchId);
                var matchUpdate = Builders<Match>.Update.Set(m => m.WinnerId, winnerId).Set(m => m.Status, "Finished");
                await _matchesCollection.UpdateOneAsync(session, matchFilter, matchUpdate);

                // 2. Pronađi turnir da vidimo u kojoj smo rundi
                var tourFilter = Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId);
                var tournament = await _tournamentsCollection.Find(session, tourFilter).FirstOrDefaultAsync();

                if (tournament == null) throw new Exception("Turnir ne postoji!");

                var currentRound = tournament.Rounds.FirstOrDefault(r => r.MatchIds.Contains(matchId));
                if (currentRound == null) throw new Exception("Meč ne pripada ovom turniru!");

                // 3. DA LI JE OVO FINALE? (Ako runda ima samo 1 meč, to je finale)
                if (currentRound.MatchIds.Count == 1)
                {
                    Console.WriteLine($"\n*** KRAJ TURNIRA! APSOLUTNI POBEDNIK JE: {winnerId} ***\n");
                    tournament.Status = "Completed";

                    // ========================================================
                    // MAGIJA: NAGRADA ZA ŠAMPIONA I SINHRONIZACIJA SVIH BAZA
                    // ========================================================
                    var winner = await _usersCollection.Find(session, u => u.Id == winnerId).FirstOrDefaultAsync();
                    if (winner != null)
                    {
                        // Računamo nove vrednosti
                        int newElo = winner.EloRating + 500;
                        int newCoins = winner.Coins + 500;
                        int newTourWins = winner.Stats.TournamentWins + 1;
                        double newTourWinRate = winner.Stats.TournamentsPlayed > 0
                            ? Math.Round((double)newTourWins / winner.Stats.TournamentsPlayed, 2)
                            : 1.0;

                        // A. MONGODB: Ažuriranje unutar transakcije
                        var winnerUpdate = Builders<UserProfile>.Update
                            .Set(u => u.EloRating, newElo)
                            .Set(u => u.Coins, newCoins)
                            .Set(u => u.Stats.TournamentWins, newTourWins)
                            .Set(u => u.Stats.TournamentWinRate, newTourWinRate);

                        await _usersCollection.UpdateOneAsync(session, u => u.Id == winnerId, winnerUpdate);

                        // B. REDIS: Odmah osvežavamo Leaderboard jer je igrač dobio +500 ELO
                        await _redisDb.SortedSetAddAsync("leaderboard_elo", winnerId, newElo);

                        // C. CASSANDRA: Beležimo ogroman skok u istoriji progresa (za grafikon)
                        var progQuery = "INSERT INTO esports.player_progress_history_by_user (user_id, recorded_at, entry_id, elo, coins, change_reason) VALUES (?, toTimestamp(now()), ?, ?, ?, ?)";
                        var preparedProg = await _cassandra.PrepareAsync(progQuery);
                        await _cassandra.ExecuteAsync(preparedProg.Bind(winnerId, Guid.NewGuid(), newElo, newCoins, "Tournament Win"));
                    }
                    // ========================================================

                    // Sačuvaj status turnira u Mongo
                    await _tournamentsCollection.ReplaceOneAsync(session, tourFilter, tournament);

                    await session.CommitTransactionAsync();
                    await _redisDb.KeyDeleteAsync($"tournament:{tournamentId}");
                    return true;
                }

                // 4. NIJE FINALE -> Idemo u sledeću rundu
                int nextRoundNumber = currentRound.RoundNumber + 1;
                var nextRound = tournament.Rounds.FirstOrDefault(r => r.RoundNumber == nextRoundNumber);

                bool tournamentUpdated = false;

                if (nextRound == null)
                {
                    // Pravimo meč za sledeću rundu (Pera prvi prošao)
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
                    // Proveri da li neko već čeka (TBD) u sledećoj rundi
                    var nextRoundMatches = await _matchesCollection
                        .Find(session, Builders<Match>.Filter.In(m => m.Id, nextRound.MatchIds))
                        .ToListAsync();

                    var waitingMatch = nextRoundMatches.FirstOrDefault(m => m.Player2Id == "TBD");

                    if (waitingMatch != null)
                    {
                        // Spajamo pobednika sa onim koji već čeka
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
                        // Pravimo novi meč u postojećoj sledećoj rundi
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
            // Prosta provera: Da li imamo paran broj igrača? (Za pravi Iks-Oks turnir obično ide 4, 8, 16...)
            if (playerIds.Count < 2 || playerIds.Count % 2 != 0)
            {
                throw new Exception("Broj igrača mora biti paran (npr. 2, 4, 8)!");
            }

            var cleanedPlayerIds = playerIds.Select(id => id.Trim()).Distinct().ToList();

            if (cleanedPlayerIds.Count != playerIds.Count)
            {
                throw new Exception("Greška: Prosledio si duple ID-jeve u Swaggeru!");
            }

            // 2. Tražimo ih u bazi
            var filter = Builders<UserProfile>.Filter.In(u => u.Id, cleanedPlayerIds);
            var existingUsers = await _usersCollection.Find(filter).ToListAsync();

            // 3. Ako se brojevi ne poklapaju, tačno ispisujemo KOJI ID fali!
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

                // 1. Spajamo igrače po dvoje i pravimo mečeve
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

                // 2. Ažuriramo Turnir (Pravimo "Rundu 1")
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
                    // Dohvatamo igrača kroz sesiju (transakciju)
                    var user = await _usersCollection.Find(session, u => u.Id == playerId).FirstOrDefaultAsync();
                    if (user != null)
                    {
                        // Računamo novo stanje (Ušao je u novi turnir)
                        int newPlayed = user.Stats.TournamentsPlayed + 1;
                        int currentWins = user.Stats.TournamentWins; // Pobede ostaju iste dok ne osvoji

                        // Računamo novi WinRate za turnire
                        double newTourWinRate = Math.Round((double)currentWins / newPlayed, 2);

                        // Pripremamo Update.Set za fizički upis u Mongo
                        var updateStats = Builders<UserProfile>.Update
                            .Set(u => u.Stats.TournamentsPlayed, newPlayed)
                            .Set(u => u.Stats.TournamentWinRate, newTourWinRate);

                        // Šaljemo u bazu unutar transakcije!
                        await _usersCollection.UpdateOneAsync(session, u => u.Id == playerId, updateStats);
                    }
                }
                // 3. Commit transakcije
                await session.CommitTransactionAsync();

                // 4. OBAVEZNO: Brišemo keš iz Redisa jer se turnir promenio!
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
            // 1. Nadji turnir
            var tournament = await _tournamentsCollection.Find(t => t.Id == tournamentId).FirstOrDefaultAsync();
            if (tournament == null) return null;

            // 2. Skupi sve ID-jeve mečeva iz svih rundi i povuci ih iz baze OĐEDNOM
            var allMatchIds = tournament.Rounds.SelectMany(r => r.MatchIds).ToList();
            var matches = await _matchesCollection.Find(m => allMatchIds.Contains(m.Id)).ToListAsync();

            // 3. Skupi sve ID-jeve igrača iz tih mečeva (Player1, Player2, Winner) i povuci ih ODJEDNOM
            var userIds = matches.Select(m => m.Player1Id)
                .Union(matches.Select(m => m.Player2Id))
                .Union(matches.Select(m => m.WinnerId))
                .Where(id => id != null && id != "TBD") // Ignorisi TBD
                .Distinct()
                .ToList();

            var users = await _usersCollection.Find(u => userIds.Contains(u.Id)).ToListAsync();

            // Pravimo "rečnik" (Dictionary) da bi C# brzo pronalazio igrača po ID-ju
            var userDict = users.ToDictionary(u => u.Id, u => new PlayerDto { Id = u.Id, Username = u.Username });

            // Dodajemo "TBD" (To Be Determined) kao lažnog igrača da bi frontend znao da se čeka
            userDict["TBD"] = new PlayerDto { Id = "TBD", Username = "Čeka se protivnik..." };

            // 4. MAPIRANJE (Stapamo sve u jedan lepi DTO objekat)
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

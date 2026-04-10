using System.Text.Json;
using Cassandra;
using EsportApi.Models;
using EsportApi.Models.DTOs;
using EsportApi.Services.Interfaces;
using MongoDB.Driver;
using StackExchange.Redis;

namespace EsportApi.Services
{
    public class GameService : IGameService
    {
        private readonly IMongoClient _mongo;
        private readonly IConnectionMultiplexer _redis;
        private readonly Cassandra.ISession _cassandra;
        private readonly ITournamentService _tournamentService;
        private readonly IMongoCollection<Match> _matchesCollection;
        private readonly IMongoCollection<UserProfile> _usersCollection;
        private readonly IMongoCollection<Tournament> _tournamentsCollection;
        private readonly ITeamService _teamService;
        private readonly IRedisRealtimePublisher _realtimePublisher;

        public GameService(
            IMongoClient mongo,
            IConnectionMultiplexer redis,
            Cassandra.ISession cassandra,
            ITournamentService tournamentService,
            ITeamService teamService,
            IRedisRealtimePublisher realtimePublisher)
        {
            _mongo = mongo;
            _redis = redis;
            _cassandra = cassandra;
            _tournamentService = tournamentService;
            var database = _mongo.GetDatabase("EsportDb");
            _matchesCollection = database.GetCollection<Match>("Matches");
            _usersCollection = database.GetCollection<UserProfile>("Users");
            _tournamentsCollection = database.GetCollection<Tournament>("Tournaments");
            _teamService = teamService;
            _realtimePublisher = realtimePublisher;
        }

        public async Task<string> StartGameAsync(string player1Id, string player2Id, string? matchId = null, string? tournamentId = null)
        {
            // Ako nam kolega prosledi turnirski matchId koristimo njega, inače pravimo novi
            var finalMatchId = matchId ?? Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;

            var game = new TicTacToeGame
            {
                Id = finalMatchId,
                TournamentId = tournamentId, // Čuvamo turnirski ID u Redisu
                Board = "_________",
                CurrentTurn = "X",
                Status = "InProgress",
                Version = 1,
                Player1Id = player1Id,
                Player2Id = player2Id
            };

            var db = _redis.GetDatabase();

            await db.HashSetAsync($"match_players:{finalMatchId}", new HashEntry[] {
                new HashEntry("P1", player1Id),
                new HashEntry("P2", player2Id),
                new HashEntry("LastMoveAt", now.Ticks.ToString())
            });

            await db.ListRemoveAsync("matchmaking_queue", player1Id, -1);
            await db.ListRemoveAsync("matchmaking_queue", player2Id, -1);
            await db.ListRemoveAsync("tournament_queue", player1Id, -1);
            await db.ListRemoveAsync("tournament_queue", player2Id, -1);

            await db.StringSetAsync($"match:{finalMatchId}", JsonSerializer.Serialize(game), TimeSpan.FromMinutes(30));
            await db.StringSetAsync($"user_active_match:{player1Id}", finalMatchId, TimeSpan.FromMinutes(30));
            await db.StringSetAsync($"user_active_match:{player2Id}", finalMatchId, TimeSpan.FromMinutes(30));
            return finalMatchId;
        }

        public async Task<TicTacToeGame> GetGameStateAsync(string matchId)
        {
            var db = _redis.GetDatabase();
            var data = await db.StringGetAsync($"match:{matchId}");
            if (data.IsNullOrEmpty)
            {
                var mongoMatch = await _matchesCollection.Find(m => m.Id == matchId).FirstOrDefaultAsync();
                if (mongoMatch == null ||
                    string.IsNullOrWhiteSpace(mongoMatch.Player1Id) ||
                    string.IsNullOrWhiteSpace(mongoMatch.Player2Id) ||
                    mongoMatch.Player2Id == "TBD" ||
                    string.Equals(mongoMatch.Status, "Finished", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                await StartGameAsync(mongoMatch.Player1Id, mongoMatch.Player2Id, mongoMatch.Id, mongoMatch.TournamentId);
                data = await db.StringGetAsync($"match:{matchId}");
                if (data.IsNullOrEmpty)
                {
                    return null;
                }
            }

            var game = JsonSerializer.Deserialize<TicTacToeGame>(data);
            if (game == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(game.Player1Id) || string.IsNullOrEmpty(game.Player2Id))
            {
                var p1Id = await db.HashGetAsync($"match_players:{matchId}", "P1");
                var p2Id = await db.HashGetAsync($"match_players:{matchId}", "P2");
                game.Player1Id = p1Id.IsNullOrEmpty ? null : p1Id.ToString();
                game.Player2Id = p2Id.IsNullOrEmpty ? null : p2Id.ToString();
            }

            return game;
        }

        public async Task<TicTacToeGame> GetMoveAsync(string matchId)
        {
            return await GetGameStateAsync(matchId);
        }

        public async Task<List<MatchHistoryItemDto>> GetMatchHistoryAsync(string userId)
        {
            var history = new List<MatchHistoryItemDto>();

            var prepared = await _cassandra.PrepareAsync(
                "SELECT played_at, match_id, opponent_username, result, symbol, is_tournament, tournament_name FROM esports.matches_history_by_user WHERE user_id = ?");
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(userId));

            foreach (var row in rows)
            {
                history.Add(new MatchHistoryItemDto
                {
                    MatchId = row.GetValue<string>("match_id"),
                    OpponentName = row.GetValue<string>("opponent_username"),
                    Result = row.GetValue<string>("result"),
                    Symbol = row.GetValue<string>("symbol"),
                    PlayedAt = row.GetValue<DateTimeOffset>("played_at").DateTime,
                    IsTournament = row.GetValue<bool>("is_tournament"),
                    TournamentName = row.IsNull("tournament_name") ? null : row.GetValue<string>("tournament_name")
                });
            }

            var mongoMatches = await _matchesCollection
                .Find(m => (m.Player1Id == userId || m.Player2Id == userId) && m.Status == "Finished")
                .SortByDescending(m => m.PlayedAt)
                .ToListAsync();

            if (mongoMatches.Count > 0)
            {
                var userIds = mongoMatches
                    .SelectMany(match => new[] { match.Player1Id, match.Player2Id })
                    .Where(id => !string.IsNullOrWhiteSpace(id) && id != "TBD")
                    .Distinct()
                    .ToList();

                var users = await _usersCollection.Find(u => userIds.Contains(u.Id)).ToListAsync();
                var usernames = users.ToDictionary(user => user.Id, user => user.Username);
                var tournamentIds = mongoMatches
                    .Where(match => !string.IsNullOrWhiteSpace(match.TournamentId))
                    .Select(match => match.TournamentId!)
                    .Distinct()
                    .ToList();
                var tournamentNames = tournamentIds.Count == 0
                    ? new Dictionary<string, string>()
                    : (await _tournamentsCollection.Find(t => tournamentIds.Contains(t.Id)).ToListAsync())
                        .ToDictionary(t => t.Id, t => t.Name);

                foreach (var match in mongoMatches)
                {
                    if (history.Any(item => item.MatchId == match.Id))
                    {
                        continue;
                    }

                    var isPlayerOne = match.Player1Id == userId;
                    var opponentId = isPlayerOne ? match.Player2Id : match.Player1Id;
                    var result = match.WinnerId == null
                        ? "Nereseno"
                        : match.WinnerId == userId
                            ? "Pobeda"
                            : "Poraz";

                    history.Add(new MatchHistoryItemDto
                    {
                        MatchId = match.Id,
                        OpponentName = usernames.GetValueOrDefault(opponentId, "Nepoznat protivnik"),
                        Result = result,
                        Symbol = isPlayerOne ? "X" : "O",
                        PlayedAt = match.PlayedAt,
                        IsTournament = !string.IsNullOrWhiteSpace(match.TournamentId),
                        TournamentName = !string.IsNullOrWhiteSpace(match.TournamentId) && tournamentNames.TryGetValue(match.TournamentId!, out var tournamentName)
                            ? tournamentName
                            : null
                    });
                }
            }

            return history
                .OrderByDescending(item => item.PlayedAt)
                .ToList();
        }

        public async Task<List<MatchMoveDto>> GetMatchMovesAsync(string matchId)
        {
            var prepared = await _cassandra.PrepareAsync(
                "SELECT moved_at, player_id, position, symbol FROM esports.moves_by_match WHERE match_id = ?");
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(matchId));
            var moveRows = rows.ToList();

            if (moveRows.Count == 0)
            {
                return new List<MatchMoveDto>();
            }

            var playerIds = moveRows
                .Select(row => row.GetValue<string>("player_id"))
                .Distinct()
                .ToList();

            var users = await _usersCollection.Find(user => playerIds.Contains(user.Id)).ToListAsync();
            var usernames = users.ToDictionary(user => user.Id, user => user.Username);

            return moveRows
                .Select((row, index) => new MatchMoveDto
                {
                    MoveNumber = index + 1,
                    PlayerName = usernames.GetValueOrDefault(row.GetValue<string>("player_id"), "Nepoznat igrac"),
                    Symbol = row.GetValue<string>("symbol"),
                    Position = row.GetValue<int>("position"),
                    MovedAt = row.GetValue<DateTimeOffset>("moved_at").DateTime
                })
                .ToList();
        }

        public async Task<string> MakeMoveAsync(string matchId, string playerId, int position, string symbol, int clientVersion)
        {
            var db = _redis.GetDatabase();
            var moveLockKey = $"lock:match:{matchId}:move";
            var moveLockValue = Guid.NewGuid().ToString();

            if (!await db.LockTakeAsync(moveLockKey, moveLockValue, TimeSpan.FromSeconds(3)))
            {
                return "Greska: Mec se trenutno azurira. Pokusaj ponovo.";
            }

            try
            {
                var game = await GetGameStateAsync(matchId);

                if (game == null)
                {
                    var mongoMatch = await _matchesCollection.Find(m => m.Id == matchId).FirstOrDefaultAsync();

                    if (mongoMatch == null) return "Greska: Mec nije pronadjen.";

                    await StartGameAsync(mongoMatch.Player1Id, mongoMatch.Player2Id, mongoMatch.Id, mongoMatch.TournamentId);
                    game = await GetGameStateAsync(matchId);
                }

                // --- FINESA 2: OPTIMISTIC CONCURRENCY (Atomska preciznost) ---
                if (game.Version != clientVersion)
                    return $"Greska: Verzija se ne poklapa (Conflict). Osvezi tabelu. Backend: {game.Version}, Tvoj: {clientVersion}";

                var p1Id = await db.HashGetAsync($"match_players:{matchId}", "P1");
                var p2Id = await db.HashGetAsync($"match_players:{matchId}", "P2");
                var expectedSymbol = playerId == p1Id.ToString()
                    ? "X"
                    : playerId == p2Id.ToString()
                        ? "O"
                        : null;

                if (expectedSymbol == null)
                    return "Greska: Nisi ucesnik ovog meca.";

                if (game.Status != "InProgress") return "Greska: Mec je zavrsen.";
                if (symbol != expectedSymbol) return "Greska: Ne mozes igrati tudjim simbolom.";
                if (game.CurrentTurn != expectedSymbol) return "Greska: Nije tvoj red.";
                if (position < 0 || position > 8 || game.Board[position] != '_') return "Greska: Polje zauzeto.";

                // --- FINESA 3: ANALITIKA BRZINE (Thinking Time) ---
                var now = DateTime.UtcNow;
                var lastMoveTicksStr = await db.HashGetAsync($"match_players:{matchId}", "LastMoveAt");
                long durationMs = 0;
                if (!lastMoveTicksStr.IsNullOrEmpty)
                {
                    var lastMoveAt = new DateTime(long.Parse(lastMoveTicksStr));
                    durationMs = (long)(now - lastMoveAt).TotalMilliseconds;
                }

                // Izvrsi potez
                char[] boardChars = game.Board.ToCharArray();
                boardChars[position] = symbol[0];
                game.Board = new string(boardChars);

                // CASSANDRA: Upis sa duration_ms
                var insertMove = "INSERT INTO esports.moves_by_match (match_id, moved_at, move_id, player_id, position, symbol, duration_ms) VALUES (?, ?, ?, ?, ?, ?, ?)";
                var statement = await _cassandra.PrepareAsync(insertMove);
                await _cassandra.ExecuteAsync(statement.Bind(matchId, now, Guid.NewGuid(), playerId, position, symbol, durationMs));

                string winnerSymbol = CheckWinner(game.Board);

                if (winnerSymbol != null)
                {
                    game.Status = winnerSymbol == "Draw" ? "Draw" : "Finished";
                    game.Version += 1;
                    string resultText = winnerSymbol == "Draw" ? "Nereseno" : $"Pobednik: {symbol}";

                    if (game.Status == "Finished")
                    {
                        await UpdateMongoStats(playerId, true);
                        string loserId = (playerId == p1Id) ? p2Id.ToString() : p1Id.ToString();
                        await UpdateMongoStats(loserId, false);

                        if (!string.IsNullOrEmpty(game.TournamentId))
                        {
                            bool advanceSuccess = await _tournamentService.AdvanceWinner(game.TournamentId, matchId, playerId);
                            if (!advanceSuccess)
                            {
                                Console.WriteLine("KRITICNO: Transakcija za turnir nije uspela!");
                            }
                        }
                    }

                    await SaveMatchHistoryForPlayersAsync(
                        matchId,
                        now,
                        p1Id.ToString(),
                        p2Id.ToString(),
                        winnerSymbol,
                        game.TournamentId);

                    var finishedMatchTtl = TimeSpan.FromMinutes(15);
                    await db.StringSetAsync($"match:{matchId}", JsonSerializer.Serialize(game), finishedMatchTtl);
                    await db.KeyExpireAsync($"match_players:{matchId}", finishedMatchTtl);
                    await ClearActiveMatchIfCurrentAsync(db, p1Id.ToString(), matchId);
                    await ClearActiveMatchIfCurrentAsync(db, p2Id.ToString(), matchId);

                    await _realtimePublisher.PublishGameFinishedAsync(matchId, resultText, game.Board);

                    return $"Kraj! {resultText}.";
                }

                game.CurrentTurn = symbol == "X" ? "O" : "X";
                game.Version += 1;

                await db.StringSetAsync($"match:{matchId}", JsonSerializer.Serialize(game), TimeSpan.FromMinutes(30));
                await db.HashSetAsync($"match_players:{matchId}", "LastMoveAt", now.Ticks.ToString());

                await _realtimePublisher.PublishMoveAsync(matchId, game);

                return "Uspesan potez.";
            }
            finally
            {
                await db.LockReleaseAsync(moveLockKey, moveLockValue);
            }
        }

        private string CheckWinner(string b)
        {
            int[][] lines = { new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 }, new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 }, new[] { 0, 4, 8 }, new[] { 2, 4, 6 } };
            foreach (var l in lines)
                if (b[l[0]] != '_' && b[l[0]] == b[l[1]] && b[l[1]] == b[l[2]]) return b[l[0]].ToString();
            return b.Contains('_') ? null : "Draw";
        }

        private async Task UpdateMongoStats(string userId, bool won)
        {
            var users = _mongo.GetDatabase("EsportDb").GetCollection<UserProfile>("Users");

            // 1. Dohvatamo trenutnog korisnika iz MongoDB-a
            var user = await users.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (user == null) return;

            // 2. Računamo novo stanje statistike (Wins, Losses, Games)
            int newTotalGames = user.Stats.TotalGames + 1;
            int newWins = won ? user.Stats.Wins + 1 : user.Stats.Wins;
            int newLosses = won ? user.Stats.Losses : user.Stats.Losses + 1;
            double newWinRate = Math.Round((double)newWins / newTotalGames, 2);

            // 3. Računamo novi ELO (Pobednik +25, Gubitnik -15)
            int eloChange = won ? 25 : -15;
            int newElo = user.EloRating + eloChange;

            // 4. Računamo nove Koine (Pobednik dobija 200)
            int newCoins = won ? user.Coins + 200 : user.Coins;

            // 5. MONGODB: Ažuriramo profil (Fizički upisujemo sve izračunate vrednosti)
            var update = Builders<UserProfile>.Update
                .Set(u => u.Stats.TotalGames, newTotalGames)
                .Set(u => u.Stats.Wins, newWins)
                .Set(u => u.Stats.Losses, newLosses)
                .Set(u => u.Stats.WinRate, newWinRate)
                .Set(u => u.EloRating, newElo)
                .Set(u => u.Coins, newCoins)
                .Set(u => u.Stats.LastGameAt, DateTime.UtcNow);

            await users.UpdateOneAsync(u => u.Id == userId, update);

            if (!string.IsNullOrEmpty(user.CurrentTeamId))
            {
                await _teamService.RecalculateTeamElo(user.CurrentTeamId);
                Console.WriteLine($"[TeamService] Osvezio sam ELO tima za klan {user.CurrentTeamId}");
            }

            // 6. REDIS: Automatsko osvežavanje globalne rang liste (Sorted Set)
            var redisDb = _redis.GetDatabase();
            await redisDb.SortedSetAddAsync("leaderboard_elo", userId, newElo);

            // 7. CASSANDRA: Beleženje istorije napretka (Time-Series za grafikone)
            // Ovo je ključno za analizu napretka kroz godine
            var progressQuery = "INSERT INTO esports.player_progress_history_by_user (user_id, recorded_at, entry_id, elo, coins, change_reason) VALUES (?, toTimestamp(now()), ?, ?, ?, ?)";
            var preparedProgress = await _cassandra.PrepareAsync(progressQuery);

            // Upisujemo u Cassandru trenutni presek stanja nakon ovog meča
            await _cassandra.ExecuteAsync(preparedProgress.Bind(userId, Guid.NewGuid(), newElo, newCoins, "Match Result"));
        }
        public async Task<List<PlayerProgress>> GetPlayerProgressAsync(string userId)
        {
            return await ReadProgressHistoryAsync(
                "SELECT recorded_at, elo, coins, change_reason FROM esports.player_progress_history_by_user WHERE user_id = ?",
                "recorded_at",
                userId);
        }
        public async Task SaveLeaderboardSnapshotAsync()
        {
            var db = _redis.GetDatabase();

            // Vučemo top 10 igrača iz Redisa (Order.Descending da bi prvi bio najbolji)
            var topPlayers = await db.SortedSetRangeByRankWithScoresAsync("leaderboard_elo", 0, 9, Order.Descending);

            if (topPlayers.Length == 0)
            {
                topPlayers = await db.SortedSetRangeByRankWithScoresAsync("leaderboard", 0, 9, Order.Descending);
            }

            // Pripremamo upit za Cassandru
            var query = "INSERT INTO esports.leaderboard_snapshots_by_date (snapshot_date, score, player_id) VALUES (toDate(now()), ?, ?)";
            var prepared = await _cassandra.PrepareAsync(query);

            foreach (var player in topPlayers)
            {
                // Upisujemo u Cassandru (Score mora biti int, PlayerId je string)
                await _cassandra.ExecuteAsync(prepared.Bind((int)player.Score, player.Element.ToString()));
            }
        }
        public async Task<string> SaveChatMessageAsync(string matchId, string playerId, string message)
        {
            var db = _redis.GetDatabase();

            // 1. BEZBEDNOST: Proveravamo ko uopšte igra ovaj meč
            var p1Id = await db.HashGetAsync($"match_players:{matchId}", "P1");
            var p2Id = await db.HashGetAsync($"match_players:{matchId}", "P2");

            // Ako meč ne postoji ili je igrač "uljez" -> odbijamo ga!
            if (p1Id.IsNullOrEmpty || p2Id.IsNullOrEmpty) return null;
            if (playerId != p1Id.ToString() && playerId != p2Id.ToString()) return null;

            // 2. Ime vadimo direktno iz baze da igrač ne bi lažirao ime na frontendu
            var user = await _mongo.GetDatabase("EsportDb").GetCollection<UserProfile>("Users")
                                   .Find(u => u.Id == playerId).FirstOrDefaultAsync();

            string username = user != null ? user.Username : "Unknown";

            // 3. Upisujemo u Redis (LPUSH + LTRIM)
            var chatKey = $"chat:{matchId}";
            var chatMessage = $"{username}: {message}";

            await db.ListLeftPushAsync(chatKey, chatMessage);
            await db.ListTrimAsync(chatKey, 0, 9);
            await db.KeyExpireAsync(chatKey, TimeSpan.FromMinutes(30));

            await _realtimePublisher.PublishChatAsync(matchId, username, message);

            return username;
        }

        public async Task<List<string>> GetChatHistoryAsync(string matchId)
        {
            var db = _redis.GetDatabase();

            // Dohvatamo sve poruke iz liste (LRANGE 0 -1)
            var messages = await db.ListRangeAsync($"chat:{matchId}");

            // Vraćamo ih kao listu stringova
            return messages.Select(m => m.ToString()).ToList();
        }

        private async Task<List<PlayerProgress>> ReadProgressHistoryAsync(string query, string timestampColumn, string userId)
        {
            var prepared = await _cassandra.PrepareAsync(query);
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(userId));
            var list = new List<PlayerProgress>();

            foreach (var row in rows)
            {
                list.Add(new PlayerProgress
                {
                    UserId = userId,
                    Timestamp = row.GetValue<DateTimeOffset>(timestampColumn).DateTime,
                    Elo = row.GetValue<int>("elo"),
                    Coins = row.GetValue<int>("coins"),
                    ChangeReason = row.GetValue<string>("change_reason")
                });
            }

            return list;
        }

        private async Task SaveMatchHistoryForPlayersAsync(
            string matchId,
            DateTime playedAt,
            string player1Id,
            string player2Id,
            string winnerSymbol,
            string? tournamentId)
        {
            var playerIds = new[] { player1Id, player2Id };
            var users = await _usersCollection.Find(user => playerIds.Contains(user.Id)).ToListAsync();
            var usernames = users.ToDictionary(user => user.Id, user => user.Username);
            string? tournamentName = null;

            if (!string.IsNullOrWhiteSpace(tournamentId))
            {
                var tournament = await _tournamentsCollection.Find(t => t.Id == tournamentId).FirstOrDefaultAsync();
                tournamentName = tournament?.Name;
            }

            var insertQuery = @"
                INSERT INTO esports.matches_history_by_user
                (user_id, played_at, match_id, opponent_id, opponent_username, result, symbol, is_tournament, tournament_name)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)";
            var prepared = await _cassandra.PrepareAsync(insertQuery);

            var player1Result = winnerSymbol == "Draw" ? "Nereseno" : winnerSymbol == "X" ? "Pobeda" : "Poraz";
            var player2Result = winnerSymbol == "Draw" ? "Nereseno" : winnerSymbol == "O" ? "Pobeda" : "Poraz";

            await _cassandra.ExecuteAsync(prepared.Bind(
                player1Id,
                playedAt,
                matchId,
                player2Id,
                usernames.GetValueOrDefault(player2Id, "Nepoznat protivnik"),
                player1Result,
                "X",
                !string.IsNullOrWhiteSpace(tournamentId),
                tournamentName));

            await _cassandra.ExecuteAsync(prepared.Bind(
                player2Id,
                playedAt,
                matchId,
                player1Id,
                usernames.GetValueOrDefault(player1Id, "Nepoznat protivnik"),
                player2Result,
                "O",
                !string.IsNullOrWhiteSpace(tournamentId),
                tournamentName));
        }

        private static async Task ClearActiveMatchIfCurrentAsync(IDatabase db, string? userId, string matchId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var key = $"user_active_match:{userId}";
            var activeMatch = await db.StringGetAsync(key);
            if (activeMatch == matchId)
            {
                await db.KeyDeleteAsync(key);
            }
        }

    }
}

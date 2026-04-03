using System.Text.Json;
using Cassandra;
using EsportApi.Hubs;
using EsportApi.Models;
using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using StackExchange.Redis;

namespace EsportApi.Services
{
    public class GameService : IGameService
    {
        private readonly IMongoClient _mongo;
        private readonly IConnectionMultiplexer _redis;
        private readonly Cassandra.ISession _cassandra;
        private readonly IHubContext<GameHub> _hubContext;
        private readonly ITournamentService _tournamentService;
        private readonly IMongoCollection<Match> _matchesCollection;

        public GameService(IMongoClient mongo, IConnectionMultiplexer redis, Cassandra.ISession cassandra, IHubContext<GameHub> hubContext, ITournamentService tournamentService)
        {
            _mongo = mongo;
            _redis = redis;
            _cassandra = cassandra;
            _hubContext = hubContext;
            _tournamentService = tournamentService;
            var database = _mongo.GetDatabase("EsportDb");
            _matchesCollection = database.GetCollection<Match>("Matches");
            InitializeCassandraTables();
            
        }

        private void InitializeCassandraTables()
        {
            _cassandra.Execute("CREATE KEYSPACE IF NOT EXISTS esports WITH replication = {'class': 'SimpleStrategy', 'replication_factor': 1}");

            // DODATO: duration_ms kolona za analitiku brzine igraca
            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.moves (
                    match_id text,
                    timestamp timestamp,
                    player_id text,
                    position int,
                    symbol text,
                    duration_ms bigint,
                    PRIMARY KEY (match_id, timestamp)
                ) WITH CLUSTERING ORDER BY (timestamp ASC)");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.matches_history (
                    match_id text PRIMARY KEY,
                    played_at timestamp,
                    result text
                )");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.leaderboard_snapshots (
                    date date,
                    score int,
                    player_id text,
                    PRIMARY KEY (date, score, player_id)
                ) WITH CLUSTERING ORDER BY (score DESC, player_id ASC)");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.player_progress_history (
                    user_id text,
                    timestamp timestamp,
                    elo int,
                    coins int,
                    change_reason text,
                    PRIMARY KEY (user_id, timestamp)
                ) WITH CLUSTERING ORDER BY (timestamp DESC)");
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
                Version = 1
            };

            var db = _redis.GetDatabase();

            await db.HashSetAsync($"match_players:{finalMatchId}", new HashEntry[] {
                new HashEntry("P1", player1Id),
                new HashEntry("P2", player2Id),
                new HashEntry("LastMoveAt", now.Ticks.ToString())
            });

            await db.StringSetAsync($"match:{finalMatchId}", JsonSerializer.Serialize(game), TimeSpan.FromMinutes(30));
            return finalMatchId;
        }

        public async Task<TicTacToeGame> GetGameStateAsync(string matchId)
        {
            var db = _redis.GetDatabase();
            var data = await db.StringGetAsync($"match:{matchId}");
            return data.IsNullOrEmpty ? null : JsonSerializer.Deserialize<TicTacToeGame>(data);
        }

        public async Task<string> MakeMoveAsync(string matchId, string playerId, int position, string symbol, int clientVersion)
        {
            var db = _redis.GetDatabase();
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

            if (game.Status != "InProgress") return "Greska: Mec je zavrsen.";
            if (game.CurrentTurn != symbol) return "Greska: Nije tvoj red.";
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
            var insertMove = "INSERT INTO esports.moves (match_id, timestamp, player_id, position, symbol, duration_ms) VALUES (?, ?, ?, ?, ?, ?)";
            var statement = await _cassandra.PrepareAsync(insertMove);
            await _cassandra.ExecuteAsync(statement.Bind(matchId, now, playerId, position, symbol, durationMs));

            string winnerSymbol = CheckWinner(game.Board);

            if (winnerSymbol != null)
            {
                game.Status = winnerSymbol == "Draw" ? "Draw" : "Finished";
                string resultText = winnerSymbol == "Draw" ? "Nereseno" : $"Pobednik: {symbol}";

                var p1Id = await db.HashGetAsync($"match_players:{matchId}", "P1");
                var p2Id = await db.HashGetAsync($"match_players:{matchId}", "P2");

                if (game.Status == "Finished")
                {
                    await UpdateMongoStats(playerId, true);
                    string loserId = (playerId == p1Id) ? p2Id.ToString() : p1Id.ToString();
                    await UpdateMongoStats(loserId, false);

                    // ==========================================
                    // INTEGRACIJA SA TURNIROM (OVO JE TVOJA IDEJA!)
                    // ==========================================
                    if (!string.IsNullOrEmpty(game.TournamentId))
                    {
                        // AUTOMATSKI zovi koleginu metodu! Nema više ručnog kucanja u Swaggeru!
                        bool advanceSuccess = await _tournamentService.AdvanceWinner(game.TournamentId, matchId, playerId);
                        if (!advanceSuccess)
                        {
                            Console.WriteLine("KRITIČNO: Transakcija za turnir nije uspela!");
                        }
                    }
                    // ==========================================
                }

                // Cassandra upis
                var endMatchQuery = "INSERT INTO esports.matches_history (match_id, played_at, result) VALUES (?, toTimestamp(now()), ?)";
                var stEnd = await _cassandra.PrepareAsync(endMatchQuery);
                await _cassandra.ExecuteAsync(stEnd.Bind(matchId, resultText));

                // Brisanje iz Redisa
                await db.KeyDeleteAsync($"match:{matchId}");
                await db.KeyDeleteAsync($"match_players:{matchId}");

                await _hubContext.Clients.Group(matchId).SendAsync("GameFinished", resultText, game.Board);

                return $"Kraj! {resultText}.";
            }

            // Nastavak
            game.CurrentTurn = symbol == "X" ? "O" : "X";
            game.Version += 1; // Povecaj verziju za sledeci potez

            // Azuriraj Redis sa novom verzijom i novim tajmstempom poslednjeg poteza
            await db.StringSetAsync($"match:{matchId}", JsonSerializer.Serialize(game), TimeSpan.FromMinutes(30));
            await db.HashSetAsync($"match_players:{matchId}", "LastMoveAt", now.Ticks.ToString());

            await _hubContext.Clients.Group(matchId).SendAsync("ReceiveMove", game);

            return "Uspesan potez.";
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

            // 6. REDIS: Automatsko osvežavanje globalne rang liste (Sorted Set)
            var redisDb = _redis.GetDatabase();
            await redisDb.SortedSetAddAsync("leaderboard_elo", userId, newElo);

            // 7. CASSANDRA: Beleženje istorije napretka (Time-Series za grafikone)
            // Ovo je ključno za analizu napretka kroz godine
            var progressQuery = "INSERT INTO esports.player_progress_history (user_id, timestamp, elo, coins, change_reason) VALUES (?, toTimestamp(now()), ?, ?, ?)";
            var preparedProgress = await _cassandra.PrepareAsync(progressQuery);

            // Upisujemo u Cassandru trenutni presek stanja nakon ovog meča
            await _cassandra.ExecuteAsync(preparedProgress.Bind(userId, newElo, newCoins, "Match Result"));
        }
        public async Task<List<PlayerProgress>> GetPlayerProgressAsync(string userId)
        {
            var list = new List<PlayerProgress>();
            var query = "SELECT timestamp, elo, coins, change_reason FROM esports.player_progress_history WHERE user_id = ?";
            var prepared = await _cassandra.PrepareAsync(query);
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(userId));

            foreach (var row in rows)
            {
                list.Add(new PlayerProgress
                {
                    UserId = userId,
                    Timestamp = row.GetValue<DateTimeOffset>("timestamp").DateTime,
                    Elo = row.GetValue<int>("elo"),
                    Coins = row.GetValue<int>("coins"),
                    ChangeReason = row.GetValue<string>("change_reason")
                });
            }
            return list;
        }
        public async Task SaveLeaderboardSnapshotAsync()
        {
            var db = _redis.GetDatabase();

            // Vučemo top 10 igrača iz Redisa (Order.Descending da bi prvi bio najbolji)
            var topPlayers = await db.SortedSetRangeByRankWithScoresAsync("leaderboard", 0, 9, Order.Descending);

            // Pripremamo upit za Cassandru
            var query = "INSERT INTO esports.leaderboard_snapshots (date, score, player_id) VALUES (toDate(now()), ?, ?)";
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

            // Vraćamo Username da bi SignalR znao ko je poslao poruku
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
    }
}
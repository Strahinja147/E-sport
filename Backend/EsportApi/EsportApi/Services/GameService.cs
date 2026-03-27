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

        public GameService(IMongoClient mongo, IConnectionMultiplexer redis, Cassandra.ISession cassandra, IHubContext<GameHub> hubContext)
        {
            _mongo = mongo;
            _redis = redis;
            _cassandra = cassandra;
            _hubContext = hubContext;
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
        }

        public async Task<string> StartGameAsync(string player1Id, string player2Id)
        {
            var matchId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;

            var game = new TicTacToeGame
            {
                Id = matchId,
                Board = "_________",
                CurrentTurn = "X",
                Status = "InProgress",
                Version = 1 // Pocetna verzija
            };

            var db = _redis.GetDatabase();

            await db.HashSetAsync($"match_players:{matchId}", new HashEntry[] {
                new HashEntry("P1", player1Id),
                new HashEntry("P2", player2Id),
                new HashEntry("LastMoveAt", now.Ticks.ToString()) // Pamtimo vreme pocetka radi prve kalkulacije
            });

            await db.StringSetAsync($"match:{matchId}", JsonSerializer.Serialize(game), TimeSpan.FromMinutes(30));
            return matchId;
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

            if (game == null) return "Greska: Mec nije pronadjen.";

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
                }

                var endMatchQuery = "INSERT INTO esports.matches_history (match_id, played_at, result) VALUES (?, toTimestamp(now()), ?)";
                var stEnd = await _cassandra.PrepareAsync(endMatchQuery);
                await _cassandra.ExecuteAsync(stEnd.Bind(matchId, resultText));

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
            var filter = Builders<UserProfile>.Filter.Eq(u => u.Id, userId);
            var update = won
                ? Builders<UserProfile>.Update.Inc(u => u.Stats.Wins, 1).Inc(u => u.Stats.TotalGames, 1).Inc(u => u.EloRating, 25).Inc(u => u.Coins, 200)
                : Builders<UserProfile>.Update.Inc(u => u.Stats.Losses, 1).Inc(u => u.Stats.TotalGames, 1).Inc(u => u.EloRating, -15);
            await users.UpdateOneAsync(filter, update);
        }
    }
}
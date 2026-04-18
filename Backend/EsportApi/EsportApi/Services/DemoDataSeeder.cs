using Cassandra;
using EsportApi.Models;
using EsportApi.Services.Interfaces;
using MongoDB.Driver;
using StackExchange.Redis;

namespace EsportApi.Services
{
    public sealed class DemoDataSeeder
    {
        private const string DemoPassword = "Demo123!";
        private const string DemoTeamId = "680000000000000000000101";

        private readonly IMongoCollection<UserProfile> _usersCollection;
        private readonly IMongoCollection<Team> _teamsCollection;
        private readonly IMongoCollection<ShopItem> _shopItemsCollection;
        private readonly ICassandraAuthService _authService;
        private readonly Cassandra.ISession _cassandra;
        private readonly IDatabase _redisDb;

        public DemoDataSeeder(
            IMongoClient mongoClient,
            ICassandraAuthService authService,
            Cassandra.ISession cassandra,
            IConnectionMultiplexer redis)
        {
            var database = mongoClient.GetDatabase("EsportDb");
            _usersCollection = database.GetCollection<UserProfile>("Users");
            _teamsCollection = database.GetCollection<Team>("Teams");
            _shopItemsCollection = database.GetCollection<ShopItem>("ShopItems");
            _authService = authService;
            _cassandra = cassandra;
            _redisDb = redis.GetDatabase();
        }

        public async Task InitializeAsync()
        {
            var users = await EnsureDemoUsersAsync();

            await EnsureFriendshipsAsync(users);
            await EnsureTeamAsync(users);
            await EnsureShopItemsAsync();
            await EnsureInventoryAndPurchasesAsync(users);
            await EnsureProgressHistoryAsync(users);
            await EnsureLoginHistoryAsync(users);
            await EnsureMatchHistoryAndMovesAsync(users);
            await EnsureLeaderboardAsync(users);
        }

        private async Task<Dictionary<string, UserProfile>> EnsureDemoUsersAsync()
        {
            var demoUsers = new[]
            {
                CreateDemoUser("680000000000000000000001", "Paja", 1520, 1600, wins: 8, losses: 3, tournamentsPlayed: 2, tournamentWins: 1),
                CreateDemoUser("680000000000000000000002", "Luka", 1485, 1200, wins: 6, losses: 4, tournamentsPlayed: 1, tournamentWins: 0),
                CreateDemoUser("680000000000000000000003", "Strale", 1450, 1100, wins: 5, losses: 4, tournamentsPlayed: 1, tournamentWins: 0),
                CreateDemoUser("680000000000000000000005", "Mika", 1325, 950, wins: 4, losses: 5, tournamentsPlayed: 1, tournamentWins: 0)
            };

            var emails = new Dictionary<string, string>
            {
                ["Paja"] = "paja@demo.local",
                ["Luka"] = "luka@demo.local",
                ["Strale"] = "strale@demo.local",
                ["Mika"] = "mika@demo.local"
            };

            foreach (var demoUser in demoUsers)
            {
                var existing = await _usersCollection.Find(user => user.Id == demoUser.Id).FirstOrDefaultAsync();
                if (existing == null)
                {
                    await _usersCollection.InsertOneAsync(demoUser);
                }
                else
                {
                    await _usersCollection.UpdateOneAsync(
                        user => user.Id == demoUser.Id,
                        Builders<UserProfile>.Update
                            .Set(user => user.Username, demoUser.Username)
                            .Set(user => user.EloRating, demoUser.EloRating)
                            .Set(user => user.Coins, demoUser.Coins)
                            .Set(user => user.Stats, demoUser.Stats)
                            .Set(user => user.CurrentTeamId, null)
                            .Set(user => user.Friends, new List<Friend>())
                            .Set(user => user.TeamInvites, new List<TeamInvite>()));
                }

                var email = emails[demoUser.Username];
                if (!await _authService.EmailExistsAsync(email))
                {
                    await _authService.RegisterAsync(email, demoUser.Id, demoUser.Username, DemoPassword);
                }
            }

            var allDemoUsers = await _usersCollection
                .Find(user => demoUsers.Select(demo => demo.Id).Contains(user.Id))
                .ToListAsync();

            return allDemoUsers.ToDictionary(user => user.Username, user => user);
        }

        private async Task EnsureFriendshipsAsync(Dictionary<string, UserProfile> users)
        {
            await EnsureAcceptedFriendshipAsync(users["Paja"], users["Luka"]);
            await EnsureAcceptedFriendshipAsync(users["Paja"], users["Strale"]);
            await EnsureAcceptedFriendshipAsync(users["Paja"], users["Mika"]);
            await EnsureAcceptedFriendshipAsync(users["Luka"], users["Strale"]);
        }

        private async Task EnsureTeamAsync(Dictionary<string, UserProfile> users)
        {
            var pendingInvite = new TeamPendingInvite
            {
                UserId = users["Strale"].Id,
                Username = users["Strale"].Username,
                RequestedByUserId = users["Paja"].Id,
                RequestedByUsername = users["Paja"].Username,
                RequestedAt = new DateTime(2026, 4, 18, 17, 0, 0, DateTimeKind.Utc)
            };

            var team = new Team
            {
                Id = DemoTeamId,
                Name = "Night Falcons",
                OwnerId = users["Paja"].Id,
                MemberIds = new List<string> { users["Paja"].Id, users["Luka"].Id },
                PendingInvites = new List<TeamPendingInvite> { pendingInvite },
                TeamElo = (users["Paja"].EloRating + users["Luka"].EloRating) / 2,
                TeamAchievements = new List<string> { "Rookie Cup finalist", "Demo roster spreman" },
                CreatedAt = new DateTime(2026, 4, 1, 16, 0, 0, DateTimeKind.Utc)
            };

            await _teamsCollection.ReplaceOneAsync(
                existingTeam => existingTeam.Id == DemoTeamId,
                team,
                new ReplaceOptions { IsUpsert = true });

            await _usersCollection.UpdateOneAsync(
                user => user.Id == users["Paja"].Id,
                Builders<UserProfile>.Update
                    .Set(user => user.CurrentTeamId, DemoTeamId)
                    .Set(user => user.TeamInvites, new List<TeamInvite>()));

            await _usersCollection.UpdateOneAsync(
                user => user.Id == users["Luka"].Id,
                Builders<UserProfile>.Update
                    .Set(user => user.CurrentTeamId, DemoTeamId)
                    .Set(user => user.TeamInvites, new List<TeamInvite>()));

            await _usersCollection.UpdateOneAsync(
                user => user.Id == users["Strale"].Id,
                Builders<UserProfile>.Update
                    .Set(user => user.CurrentTeamId, null)
                    .Set(user => user.TeamInvites, new List<TeamInvite>
                    {
                        new()
                        {
                            TeamId = DemoTeamId,
                            TeamName = team.Name,
                            RequestedByUserId = users["Paja"].Id,
                            RequestedByUsername = users["Paja"].Username,
                            RequestedAt = pendingInvite.RequestedAt
                        }
                    }));

            await _usersCollection.UpdateOneAsync(
                user => user.Id == users["Mika"].Id,
                Builders<UserProfile>.Update
                    .Set(user => user.CurrentTeamId, null)
                    .Set(user => user.TeamInvites, new List<TeamInvite>()));
        }

        private async Task EnsureShopItemsAsync()
        {
            var items = new[]
            {
                new ShopItem { Id = "680000000000000000000201", Name = "Zlatni X Skin", Price = 300, IsLimited = false, InitialStock = 0, CurrentStock = 0 },
                new ShopItem { Id = "680000000000000000000202", Name = "Vatreni O Skin", Price = 450, IsLimited = false, InitialStock = 0, CurrentStock = 0 },
                new ShopItem { Id = "680000000000000000000203", Name = "Neon Matrix Board", Price = 1000, IsLimited = false, InitialStock = 0, CurrentStock = 0 },
                new ShopItem { Id = "680000000000000000000204", Name = "E-Sport Champion Cape", Price = 5000, IsLimited = false, InitialStock = 0, CurrentStock = 0 },
                new ShopItem { Id = "680000000000000000000205", Name = "Zlatni X (Limited Edition)", Price = 1000, IsLimited = true, InitialStock = 2, CurrentStock = 2 }
            };

            foreach (var item in items)
            {
                await _shopItemsCollection.ReplaceOneAsync(
                    shopItem => shopItem.Id == item.Id,
                    item,
                    new ReplaceOptions { IsUpsert = true });
            }

            await _redisDb.StringSetAsync("item_stock:680000000000000000000205", 2);
        }

        private async Task EnsureInventoryAndPurchasesAsync(Dictionary<string, UserProfile> users)
        {
            await SeedInventoryItemAsync(
                users["Paja"].Id,
                "680000000000000000000201",
                "Zlatni X Skin",
                300,
                new DateTime(2026, 4, 1, 18, 0, 0, DateTimeKind.Utc),
                Guid.Parse("10000000-0000-0000-0000-000000000001"));

            await SeedInventoryItemAsync(
                users["Paja"].Id,
                "680000000000000000000203",
                "Neon Matrix Board",
                1000,
                new DateTime(2026, 4, 10, 20, 10, 0, DateTimeKind.Utc),
                Guid.Parse("10000000-0000-0000-0000-000000000002"));

            await SeedInventoryItemAsync(
                users["Luka"].Id,
                "680000000000000000000202",
                "Vatreni O Skin",
                450,
                new DateTime(2026, 4, 2, 19, 15, 0, DateTimeKind.Utc),
                Guid.Parse("10000000-0000-0000-0000-000000000003"));

            await SeedInventoryItemAsync(
                users["Strale"].Id,
                "680000000000000000000201",
                "Zlatni X Skin",
                300,
                new DateTime(2026, 4, 5, 21, 5, 0, DateTimeKind.Utc),
                Guid.Parse("10000000-0000-0000-0000-000000000004"));

            await SeedInventoryItemAsync(
                users["Mika"].Id,
                "680000000000000000000202",
                "Vatreni O Skin",
                450,
                new DateTime(2026, 4, 7, 17, 45, 0, DateTimeKind.Utc),
                Guid.Parse("10000000-0000-0000-0000-000000000005"));
        }

        private async Task EnsureProgressHistoryAsync(Dictionary<string, UserProfile> users)
        {
            await SeedProgressRowAsync(users["Paja"].Id, new DateTime(2026, 3, 23, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000001"), 1450, 900, "Match Result");
            await SeedProgressRowAsync(users["Paja"].Id, new DateTime(2026, 3, 28, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000002"), 1480, 1100, "Match Result");
            await SeedProgressRowAsync(users["Paja"].Id, new DateTime(2026, 4, 3, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000003"), 1505, 1300, "Tournament Win");
            await SeedProgressRowAsync(users["Paja"].Id, new DateTime(2026, 4, 8, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000004"), 1520, 1600, "Match Result");

            await SeedProgressRowAsync(users["Luka"].Id, new DateTime(2026, 3, 25, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000005"), 1420, 900, "Match Result");
            await SeedProgressRowAsync(users["Luka"].Id, new DateTime(2026, 4, 5, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000006"), 1485, 1200, "Match Result");

            await SeedProgressRowAsync(users["Strale"].Id, new DateTime(2026, 3, 24, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000007"), 1360, 820, "Match Result");
            await SeedProgressRowAsync(users["Strale"].Id, new DateTime(2026, 4, 4, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000008"), 1415, 980, "Match Result");
            await SeedProgressRowAsync(users["Strale"].Id, new DateTime(2026, 4, 9, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000009"), 1450, 1100, "Team Scrim Win");

            await SeedProgressRowAsync(users["Mika"].Id, new DateTime(2026, 3, 22, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000010"), 1210, 700, "Match Result");
            await SeedProgressRowAsync(users["Mika"].Id, new DateTime(2026, 4, 2, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000011"), 1260, 820, "Match Result");
            await SeedProgressRowAsync(users["Mika"].Id, new DateTime(2026, 4, 11, 18, 0, 0, DateTimeKind.Utc), Guid.Parse("20000000-0000-0000-0000-000000000012"), 1325, 950, "Match Result");
        }

        private async Task EnsureLoginHistoryAsync(Dictionary<string, UserProfile> users)
        {
            await SeedLoginRowAsync(users["Paja"].Id, new DateTime(2026, 4, 17, 19, 10, 0, DateTimeKind.Utc), Guid.Parse("30000000-0000-0000-0000-000000000001"), "127.0.0.1", "Chrome on Windows");
            await SeedLoginRowAsync(users["Paja"].Id, new DateTime(2026, 4, 18, 17, 45, 0, DateTimeKind.Utc), Guid.Parse("30000000-0000-0000-0000-000000000002"), "127.0.0.1", "Firefox on Windows");

            await SeedLoginRowAsync(users["Luka"].Id, new DateTime(2026, 4, 16, 21, 5, 0, DateTimeKind.Utc), Guid.Parse("30000000-0000-0000-0000-000000000003"), "127.0.0.1", "Edge on Windows");
            await SeedLoginRowAsync(users["Luka"].Id, new DateTime(2026, 4, 18, 16, 20, 0, DateTimeKind.Utc), Guid.Parse("30000000-0000-0000-0000-000000000004"), "127.0.0.1", "Chrome on Windows");

            await SeedLoginRowAsync(users["Strale"].Id, new DateTime(2026, 4, 15, 20, 35, 0, DateTimeKind.Utc), Guid.Parse("30000000-0000-0000-0000-000000000005"), "127.0.0.1", "Firefox on Windows");
            await SeedLoginRowAsync(users["Strale"].Id, new DateTime(2026, 4, 18, 18, 5, 0, DateTimeKind.Utc), Guid.Parse("30000000-0000-0000-0000-000000000006"), "127.0.0.1", "Chrome on Windows");

            await SeedLoginRowAsync(users["Mika"].Id, new DateTime(2026, 4, 14, 19, 50, 0, DateTimeKind.Utc), Guid.Parse("30000000-0000-0000-0000-000000000007"), "127.0.0.1", "Edge on Windows");
            await SeedLoginRowAsync(users["Mika"].Id, new DateTime(2026, 4, 18, 15, 40, 0, DateTimeKind.Utc), Guid.Parse("30000000-0000-0000-0000-000000000008"), "127.0.0.1", "Firefox on Windows");
        }

        private async Task EnsureMatchHistoryAndMovesAsync(Dictionary<string, UserProfile> users)
        {
            await SeedFinishedMatchAsync(
                matchId: "demo-match-paja-luka-1",
                playedAt: new DateTime(2026, 4, 6, 18, 30, 0, DateTimeKind.Utc),
                winner: users["Paja"],
                loser: users["Luka"],
                winningSymbol: "X",
                isTournament: false,
                tournamentName: null,
                moves: new[]
                {
                    CreateMove(users["Paja"].Id, 0, "X", 1400),
                    CreateMove(users["Luka"].Id, 4, "O", 2100),
                    CreateMove(users["Paja"].Id, 1, "X", 1800),
                    CreateMove(users["Luka"].Id, 8, "O", 2500),
                    CreateMove(users["Paja"].Id, 2, "X", 1700)
                },
                firstMoveAt: new DateTime(2026, 4, 6, 18, 26, 0, DateTimeKind.Utc),
                firstMoveId: Guid.Parse("40000000-0000-0000-0000-000000000001"));

            await SeedFinishedMatchAsync(
                matchId: "demo-match-strale-mika-1",
                playedAt: new DateTime(2026, 4, 9, 20, 10, 0, DateTimeKind.Utc),
                winner: users["Strale"],
                loser: users["Mika"],
                winningSymbol: "O",
                isTournament: false,
                tournamentName: null,
                moves: new[]
                {
                    CreateMove(users["Mika"].Id, 0, "X", 2200),
                    CreateMove(users["Strale"].Id, 4, "O", 1800),
                    CreateMove(users["Mika"].Id, 1, "X", 2000),
                    CreateMove(users["Strale"].Id, 8, "O", 1600),
                    CreateMove(users["Mika"].Id, 2, "X", 2100),
                    CreateMove(users["Strale"].Id, 6, "O", 1500)
                },
                firstMoveAt: new DateTime(2026, 4, 9, 20, 4, 0, DateTimeKind.Utc),
                firstMoveId: Guid.Parse("40000000-0000-0000-0000-000000000101"));

            await SeedFinishedMatchAsync(
                matchId: "demo-match-paja-strale-tournament-1",
                playedAt: new DateTime(2026, 4, 12, 19, 0, 0, DateTimeKind.Utc),
                winner: users["Paja"],
                loser: users["Strale"],
                winningSymbol: "O",
                isTournament: true,
                tournamentName: "Spring Pulse Cup",
                moves: new[]
                {
                    CreateMove(users["Strale"].Id, 0, "X", 1700),
                    CreateMove(users["Paja"].Id, 4, "O", 1500),
                    CreateMove(users["Strale"].Id, 2, "X", 1650),
                    CreateMove(users["Paja"].Id, 3, "O", 1550),
                    CreateMove(users["Strale"].Id, 1, "X", 1800),
                    CreateMove(users["Paja"].Id, 5, "O", 1400)
                },
                firstMoveAt: new DateTime(2026, 4, 12, 18, 54, 0, DateTimeKind.Utc),
                firstMoveId: Guid.Parse("40000000-0000-0000-0000-000000000201"));
        }

        private async Task EnsureLeaderboardAsync(Dictionary<string, UserProfile> users)
        {
            foreach (var user in users.Values)
            {
                await _redisDb.SortedSetAddAsync("leaderboard_elo", user.Id, user.EloRating);
            }
        }

        private async Task EnsureAcceptedFriendshipAsync(UserProfile firstUser, UserProfile secondUser)
        {
            await ReplaceFriendEntryAsync(firstUser.Id, new Friend
            {
                UserId = secondUser.Id,
                Username = secondUser.Username,
                Status = "Accepted",
                RequestedByUserId = firstUser.Id
            });

            await ReplaceFriendEntryAsync(secondUser.Id, new Friend
            {
                UserId = firstUser.Id,
                Username = firstUser.Username,
                Status = "Accepted",
                RequestedByUserId = firstUser.Id
            });
        }

        private async Task ReplaceFriendEntryAsync(string userId, Friend friend)
        {
            await _usersCollection.UpdateOneAsync(
                user => user.Id == userId,
                Builders<UserProfile>.Update.PullFilter(user => user.Friends, entry => entry.UserId == friend.UserId));

            await _usersCollection.UpdateOneAsync(
                user => user.Id == userId,
                Builders<UserProfile>.Update.Push(user => user.Friends, friend));
        }

        private async Task SeedFinishedMatchAsync(
            string matchId,
            DateTime playedAt,
            UserProfile winner,
            UserProfile loser,
            string winningSymbol,
            bool isTournament,
            string? tournamentName,
            DemoMoveSeed[] moves,
            DateTime firstMoveAt,
            Guid firstMoveId)
        {
            var losingSymbol = winningSymbol == "X" ? "O" : "X";

            await SeedMatchHistoryRowAsync(winner, loser, matchId, playedAt, "Pobeda", winningSymbol, isTournament, tournamentName);
            await SeedMatchHistoryRowAsync(loser, winner, matchId, playedAt, "Poraz", losingSymbol, isTournament, tournamentName);

            for (var index = 0; index < moves.Length; index++)
            {
                await SeedMoveAsync(
                    matchId,
                    firstMoveAt.AddMinutes(index + 1),
                    IncrementGuid(firstMoveId, index),
                    moves[index].PlayerId,
                    moves[index].Position,
                    moves[index].Symbol,
                    moves[index].DurationMs);
            }
        }

        private static Guid IncrementGuid(Guid baseGuid, int offset)
        {
            var bytes = baseGuid.ToByteArray();
            var carry = offset;

            for (var index = bytes.Length - 1; index >= 0 && carry > 0; index--)
            {
                var value = bytes[index] + carry;
                bytes[index] = (byte)(value & 0xFF);
                carry = value >> 8;
            }

            return new Guid(bytes);
        }

        private static DemoMoveSeed CreateMove(string playerId, int position, string symbol, long durationMs)
        {
            return new DemoMoveSeed(playerId, position, symbol, durationMs);
        }

        private async Task SeedInventoryItemAsync(
            string userId,
            string itemId,
            string itemName,
            int purchasePrice,
            DateTime purchasedAt,
            Guid purchaseId)
        {
            var inventoryByUserQuery =
                "INSERT INTO esports.inventory_by_user (user_id, purchased_at, item_id, item_name, purchase_price) VALUES (?, ?, ?, ?, ?)";
            var inventoryByUserPrepared = await _cassandra.PrepareAsync(inventoryByUserQuery);
            await _cassandra.ExecuteAsync(inventoryByUserPrepared.Bind(userId, purchasedAt, itemId, itemName, purchasePrice));

            var inventoryLookupQuery =
                "INSERT INTO esports.inventory_items_by_user (user_id, item_id, item_name, purchased_at, purchase_price) VALUES (?, ?, ?, ?, ?)";
            var inventoryLookupPrepared = await _cassandra.PrepareAsync(inventoryLookupQuery);
            await _cassandra.ExecuteAsync(inventoryLookupPrepared.Bind(userId, itemId, itemName, purchasedAt, purchasePrice));

            var monthKey = purchasedAt.ToString("yyyy-MM");
            var purchaseLogQuery =
                "INSERT INTO esports.purchase_logs_by_month (year_month, purchased_at, purchase_id, user_id, item_id, item_name, price) VALUES (?, ?, ?, ?, ?, ?, ?)";
            var purchaseLogPrepared = await _cassandra.PrepareAsync(purchaseLogQuery);
            await _cassandra.ExecuteAsync(purchaseLogPrepared.Bind(monthKey, purchasedAt, purchaseId, userId, itemId, itemName, purchasePrice));
        }

        private async Task SeedProgressRowAsync(string userId, DateTime recordedAt, Guid entryId, int elo, int coins, string reason)
        {
            var query =
                "INSERT INTO esports.player_progress_history_by_user (user_id, recorded_at, entry_id, elo, coins, change_reason) VALUES (?, ?, ?, ?, ?, ?)";
            var prepared = await _cassandra.PrepareAsync(query);
            await _cassandra.ExecuteAsync(prepared.Bind(userId, recordedAt, entryId, elo, coins, reason));
        }

        private async Task SeedLoginRowAsync(string userId, DateTime loggedAt, Guid entryId, string ipAddress, string device)
        {
            var query =
                "INSERT INTO esports.login_history_by_user (user_id, logged_at, entry_id, ip_address, device) VALUES (?, ?, ?, ?, ?)";
            var prepared = await _cassandra.PrepareAsync(query);
            await _cassandra.ExecuteAsync(prepared.Bind(userId, loggedAt, entryId, ipAddress, device));
        }

        private async Task SeedMatchHistoryRowAsync(
            UserProfile user,
            UserProfile opponent,
            string matchId,
            DateTime playedAt,
            string result,
            string symbol,
            bool isTournament,
            string? tournamentName)
        {
            var query = @"
                INSERT INTO esports.matches_history_by_user
                (user_id, played_at, match_id, opponent_id, opponent_username, result, symbol, is_tournament, tournament_name)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)";
            var prepared = await _cassandra.PrepareAsync(query);
            await _cassandra.ExecuteAsync(prepared.Bind(
                user.Id,
                playedAt,
                matchId,
                opponent.Id,
                opponent.Username,
                result,
                symbol,
                isTournament,
                tournamentName));
        }

        private async Task SeedMoveAsync(
            string matchId,
            DateTime movedAt,
            Guid moveId,
            string playerId,
            int position,
            string symbol,
            long durationMs)
        {
            var query =
                "INSERT INTO esports.moves_by_match (match_id, moved_at, move_id, player_id, position, symbol, duration_ms) VALUES (?, ?, ?, ?, ?, ?, ?)";
            var prepared = await _cassandra.PrepareAsync(query);
            await _cassandra.ExecuteAsync(prepared.Bind(matchId, movedAt, moveId, playerId, position, symbol, durationMs));
        }

        private static UserProfile CreateDemoUser(
            string id,
            string username,
            int elo,
            int coins,
            int wins,
            int losses,
            int tournamentsPlayed,
            int tournamentWins)
        {
            var totalGames = wins + losses;
            return new UserProfile
            {
                Id = id,
                Username = username,
                EloRating = elo,
                Coins = coins,
                CurrentTeamId = null,
                Friends = new List<Friend>(),
                TeamInvites = new List<TeamInvite>(),
                Stats = new PlayerStatistics
                {
                    TotalGames = totalGames,
                    Wins = wins,
                    Losses = losses,
                    TournamentsPlayed = tournamentsPlayed,
                    TournamentWins = tournamentWins,
                    TournamentWinRate = tournamentsPlayed == 0 ? 0 : Math.Round((double)tournamentWins / tournamentsPlayed, 2),
                    WinRate = totalGames == 0 ? 0 : Math.Round((double)wins / totalGames, 2),
                    LastGameAt = new DateTime(2026, 4, 18, 18, 0, 0, DateTimeKind.Utc)
                }
            };
        }

        private sealed record DemoMoveSeed(string PlayerId, int Position, string Symbol, long DurationMs);
    }
}

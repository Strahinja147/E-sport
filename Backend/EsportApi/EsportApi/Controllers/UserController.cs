using Cassandra;
using EsportApi.Services;
using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using StackExchange.Redis;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IMongoCollection<UserProfile> _usersCollection;
        private readonly IGameService _gameService;
        private readonly Cassandra.ISession _cassandra;
        private readonly IDatabase _redisDb; // DODATO
        private readonly IUserService _userService;

        // Ažuriran konstruktor
        public UserController(IMongoClient mongoClient, IGameService gameService, IConnectionMultiplexer redis, IUserService userService, Cassandra.ISession cassandra)
        {
            var database = mongoClient.GetDatabase("EsportDb");
            _usersCollection = database.GetCollection<UserProfile>("Users");
            _gameService = gameService;
            _cassandra = cassandra;
            _redisDb = redis.GetDatabase(); // DODATO
            _userService = userService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(string username)
        {
            var existingUser = await _usersCollection.Find(u => u.Username.ToLower() == username.ToLower()).FirstOrDefaultAsync();
            if (existingUser != null) return BadRequest("Korisnik već postoji.");

            var newUser = new UserProfile
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Username = username,
                EloRating = 1000,
                Coins = 500,
                Stats = new PlayerStatistics { Wins = 0, Losses = 0, TotalGames = 0, WinRate = 0, LastGameAt = DateTime.UtcNow }
            };

            await _usersCollection.InsertOneAsync(newUser);
            return Ok(newUser);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("Korisničko ime je obavezno.");

            var user = await _usersCollection.Find(u => u.Username.ToLower() == username.ToLower()).FirstOrDefaultAsync();

            if (user == null)
            {
                return NotFound("Korisnik sa tim imenom ne postoji. Registruj se prvo.");
            }

            await _redisDb.SetAddAsync("online_players", user.Id);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (ip == "::1") ip = "127.0.0.1";

            var device = Request.Headers["User-Agent"].ToString() ?? "Unknown Device";

            var query = "INSERT INTO esports.login_history (user_id, timestamp, ip_address, device) VALUES (?, toTimestamp(now()), ?, ?)";
            var prepared = await _cassandra.PrepareAsync(query);
            await _cassandra.ExecuteAsync(prepared.Bind(user.Id, ip, device));

            return Ok(user);
        }

        // ==========================================
        // NOVE RUTE ZA ONLINE STATUS
        // ==========================================
        [HttpPost("logout")]
        public async Task<IActionResult> Logout(string userId)
        {
            // SREM: Brišemo igrača iz Seta
            await _redisDb.SetRemoveAsync("online_players", userId);
            return Ok("Izlogovan uspešno.");
        }
        [HttpGet("online-count")]
        public async Task<IActionResult> GetOnlineCount()
        {
            // SCARD: Ultra-brza komanda koja vraća broj članova u Setu (O(1) kompleksnost)
            var count = await _redisDb.SetLengthAsync("online_players");
            return Ok(new { OnlinePlayers = count });
        }


        [HttpGet("audit-logs/{userId}")]
        public async Task<IActionResult> GetAuditLogs(string userId)
        {
            var query = "SELECT timestamp, ip_address, device FROM esports.login_history WHERE user_id = ?";
            var prepared = await _cassandra.PrepareAsync(query);
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(userId));

            var logs = rows.Select(r => new {
                Time = r.GetValue<DateTimeOffset>("timestamp"),
                IP = r.GetValue<string>("ip_address"),
                Device = r.GetValue<string>("device")
            }).ToList();

            return Ok(logs);
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var users = await _usersCollection.Find(_ => true).ToListAsync();
            return Ok(users);
        }

        [HttpGet("progress/{userId}")]
        public async Task<IActionResult> GetPlayerProgress(string userId)
        {
            var history = await _gameService.GetPlayerProgressAsync(userId);
            return Ok(history);
        }

        [HttpPost("send-friend-request")]
        public async Task<IActionResult> SendRequest(string senderId, string receiverId)
        {
            try
            {
                var success = await _userService.SendFriendRequest(senderId, receiverId);
                return success ? Ok("Zahtev za prijateljstvo uspešno poslat!") : BadRequest("Greška pri slanju zahteva.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("accept-friend-request")]
        public async Task<IActionResult> AcceptRequest(string myUserId, string friendId)
        {
            try
            {
                var success = await _userService.AcceptFriendRequest(myUserId, friendId);
                return success ? Ok("Prijateljstvo prihvaćeno!") : BadRequest("Greška pri prihvatanju.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
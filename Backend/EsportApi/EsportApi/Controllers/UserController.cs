using Cassandra;
using EsportApi.Models.DTOs;
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
        private readonly IDatabase _redisDb;
        private readonly IUserService _userService;
        private readonly ICassandraAuthService _authService;

        public UserController(
            IMongoClient mongoClient,
            IGameService gameService,
            IConnectionMultiplexer redis,
            IUserService userService,
            Cassandra.ISession cassandra,
            ICassandraAuthService authService)
        {
            var database = mongoClient.GetDatabase("EsportDb");
            _usersCollection = database.GetCollection<UserProfile>("Users");
            _gameService = gameService;
            _cassandra = cassandra;
            _redisDb = redis.GetDatabase();
            _userService = userService;
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var username = request.Username.Trim();
            var email = _authService.NormalizeEmail(request.Email);

            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("Korisnicko ime je obavezno.");

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email je obavezan.");

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                return BadRequest("Lozinka mora imati najmanje 8 karaktera.");

            var existingUser = await _usersCollection.Find(u => u.Username.ToLower() == username.ToLower()).FirstOrDefaultAsync();
            if (existingUser != null)
                return BadRequest("Korisnik vec postoji.");

            if (await _authService.EmailExistsAsync(email))
                return BadRequest("Nalog sa tim email-om vec postoji.");

            var newUser = new UserProfile
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Username = username,
                EloRating = 1000,
                Coins = 500,
                Stats = new PlayerStatistics
                {
                    Wins = 0,
                    Losses = 0,
                    TotalGames = 0,
                    WinRate = 0,
                    LastGameAt = DateTime.UtcNow
                }
            };

            await _usersCollection.InsertOneAsync(newUser);

            try
            {
                await _authService.RegisterAsync(email, newUser.Id, newUser.Username, request.Password);
            }
            catch
            {
                await _usersCollection.DeleteOneAsync(u => u.Id == newUser.Id);
                throw;
            }

            await _redisDb.SetAddAsync("online_players", newUser.Id);

            return Ok(newUser);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var email = _authService.NormalizeEmail(request.Email);

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email je obavezan.");

            if (string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Lozinka je obavezna.");

            var authUser = await _authService.ValidateCredentialsAsync(email, request.Password);
            if (authUser == null)
            {
                return Unauthorized("Pogresan email ili lozinka.");
            }

            var user = await _usersCollection.Find(u => u.Id == authUser.UserId).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound("Profil za ovaj nalog nije pronadjen.");
            }

            await _redisDb.SetAddAsync("online_players", user.Id);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (ip == "::1") ip = "127.0.0.1";

            var device = Request.Headers["User-Agent"].ToString() ?? "Unknown Device";

            var query = "INSERT INTO esports.login_history_by_user (user_id, logged_at, entry_id, ip_address, device) VALUES (?, toTimestamp(now()), ?, ?, ?)";
            var prepared = await _cassandra.PrepareAsync(query);
            await _cassandra.ExecuteAsync(prepared.Bind(user.Id, Guid.NewGuid(), ip, device));

            return Ok(user);
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(string userId)
        {
            await _redisDb.SetRemoveAsync("online_players", userId);
            return Ok("Izlogovan uspesno.");
        }

        [HttpGet("online-count")]
        public async Task<IActionResult> GetOnlineCount()
        {
            var count = await _redisDb.SetLengthAsync("online_players");
            return Ok(new { OnlinePlayers = count });
        }

        [HttpGet("audit-logs/{userId}")]
        public async Task<IActionResult> GetAuditLogs(string userId)
        {
            var (rows, timeColumn) = await ReadAuditRowsAsync(userId);

            var logs = rows.Select(r => new
            {
                Time = r.GetValue<DateTimeOffset>(timeColumn),
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
                return success ? Ok("Zahtev za prijateljstvo uspesno poslat!") : BadRequest("Greska pri slanju zahteva.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("send-friend-request-by-username")]
        public async Task<IActionResult> SendRequestByUsername([FromBody] FriendLookupRequest request)
        {
            try
            {
                var success = await _userService.SendFriendRequestByUsername(request.SenderId, request.Username);
                return success ? Ok("Zahtev za prijateljstvo uspesno poslat!") : BadRequest("Greska pri slanju zahteva.");
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
                return success ? Ok("Prijateljstvo prihvaceno!") : BadRequest("Greska pri prihvatanju.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("reject-friend-request")]
        public async Task<IActionResult> RejectRequest(string myUserId, string senderId)
        {
            try
            {
                var success = await _userService.RejectFriendRequest(myUserId, senderId);
                return success ? Ok("Zahtev za prijateljstvo odbijen!") : BadRequest("Greska pri odbijanju.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("remove-friend")]
        public async Task<IActionResult> RemoveFriend(string myUserId, string friendId)
        {
            try
            {
                var success = await _userService.RemoveFriend(myUserId, friendId);
                return success ? Ok("Prijatelj obrisan sa liste!") : BadRequest("Greska pri brisanju prijatelja.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("online-friends/{userId}")]
        public async Task<IActionResult> GetOnlineFriends(string userId)
        {
            var onlineFriendIds = await _userService.GetOnlineFriendIds(userId);
            return Ok(onlineFriendIds);
        }

        private async Task<(RowSet Rows, string TimeColumn)> ReadAuditRowsAsync(string userId)
        {
            var query = "SELECT logged_at, ip_address, device FROM esports.login_history_by_user WHERE user_id = ?";
            var prepared = await _cassandra.PrepareAsync(query);
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(userId));

            if (rows.Any())
            {
                return (rows, "logged_at");
            }

            var legacyPrepared = await _cassandra.PrepareAsync(
                "SELECT timestamp, ip_address, device FROM esports.login_history WHERE user_id = ?");
            return (await _cassandra.ExecuteAsync(legacyPrepared.Bind(userId)), "timestamp");
        }
    }
}

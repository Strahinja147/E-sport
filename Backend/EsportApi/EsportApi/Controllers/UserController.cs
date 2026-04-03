using EsportApi.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IMongoCollection<UserProfile> _usersCollection;
        private readonly IGameService _gameService;

        public UserController(IMongoClient mongoClient, IGameService gameService)
        {
            var database = mongoClient.GetDatabase("EsportDb");
            _usersCollection = database.GetCollection<UserProfile>("Users");
            _gameService = gameService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(string username)
        {
            // Provera da li korisnik već postoji pre registracije (da nemamo dva Pere)
            var existingUser = await _usersCollection.Find(u => u.Username.ToLower() == username.ToLower()).FirstOrDefaultAsync();
            if (existingUser != null)
            {
                return BadRequest("Korisnik sa tim imenom već postoji. Pokušaj da se uloguješ.");
            }

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
            return Ok(newUser);
        }

        // ==========================================
        // NOVO: RUTA ZA LOGOVANJE I DOHVATANJE ID-ja
        // ==========================================
        [HttpPost("login")]
        public async Task<IActionResult> Login(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("Korisničko ime je obavezno.");

            // Tražimo korisnika u Mongo bazi (ignorišemo velika/mala slova radi lakšeg korišćenja)
            var user = await _usersCollection.Find(u => u.Username.ToLower() == username.ToLower()).FirstOrDefaultAsync();

            if (user == null)
            {
                return NotFound("Korisnik sa tim imenom ne postoji. Registruj se prvo.");
            }

            // Vraćamo ceo profil korisnika. Frontend će odavde izvući 'id', 'eloRating', 'coins' itd.
            return Ok(user);
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
            // Koristimo GameService jer smo tamo stavili metodu (možeš je i premestiti)
            var history = await _gameService.GetPlayerProgressAsync(userId);
            return Ok(history);
        }
    }
}
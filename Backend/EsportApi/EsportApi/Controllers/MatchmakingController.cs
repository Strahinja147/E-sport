using EsportApi.Models;
using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MatchmakingController : ControllerBase
    {
        private readonly IMatchmakingService _matchService;
        private readonly IMongoClient _mongoClient;
        private readonly ICassandraAuthService _authService;

        public MatchmakingController(
            IMatchmakingService matchService,
            IMongoClient mongoClient,
            ICassandraAuthService authService)
        {
            _matchService = matchService;
            _mongoClient = mongoClient;
            _authService = authService;
        }

        [HttpPost("join")]
        public async Task<IActionResult> Join(string userId)
        {
            try
            {
                await _matchService.AddToQueue(userId);
                return Ok("Joined queue.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("check-match")]
        public async Task<IActionResult> Check(string? userId = null)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var assignedMatch = await _matchService.GetAssignedMatchAsync(userId);
                return assignedMatch != null
                    ? Ok(assignedMatch)
                    : Ok(new { Message = "Still waiting for players..." });
            }

            var match = await _matchService.TryMatch();

            return match != null ? Ok(match) : Ok(new { Message = "Still waiting for players..." });
        }

        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetLeaderboard(int count = 10)
        {
            var board = await _matchService.GetTopPlayers(count);
            return Ok(board);
        }

        [HttpPost("seed-users")]
        public async Task<IActionResult> SeedUsers(string username, string? email = null, string? password = null)
        {
            var collection = _mongoClient.GetDatabase("EsportDb").GetCollection<UserProfile>("Users");
            var normalizedUsername = username.Trim();
            var normalizedEmail = _authService.NormalizeEmail(
                string.IsNullOrWhiteSpace(email) ? $"{normalizedUsername.ToLowerInvariant()}@pulse-arena.local" : email);
            var effectivePassword = string.IsNullOrWhiteSpace(password) ? "Test123!" : password;

            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                return BadRequest("Korisnicko ime je obavezno.");
            }

            var existingUser = await collection.Find(u => u.Username.ToLower() == normalizedUsername.ToLower()).FirstOrDefaultAsync();
            if (existingUser != null)
            {
                return BadRequest("Korisnik sa tim imenom vec postoji.");
            }

            if (await _authService.EmailExistsAsync(normalizedEmail))
            {
                return BadRequest("Nalog sa tim email-om vec postoji.");
            }

            var newUser = new UserProfile
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Username = normalizedUsername
            };

            await collection.InsertOneAsync(newUser);

            try
            {
                await _authService.RegisterAsync(normalizedEmail, newUser.Id, newUser.Username, effectivePassword);
            }
            catch
            {
                await collection.DeleteOneAsync(u => u.Id == newUser.Id);
                throw;
            }

            return Ok(new
            {
                User = newUser,
                Email = normalizedEmail,
                TemporaryPassword = effectivePassword
            });
        }

        [HttpPost("sync")]
        public async Task<IActionResult> Sync()
        {
            await _matchService.SyncLeaderboardAsync();
            return Ok("Svi igraci su prebaceni u Redis Leaderboard!");
        }

        [HttpPost("join-tournament")]
        public async Task<IActionResult> JoinTournament(string userId)
        {
            var result = await _matchService.JoinTournamentQueueAsync(userId);

            if (result.Contains("Nedovoljan") || result.Contains("ne postoji") || result.Contains("Vec si"))
                return BadRequest(new { Message = result });

            return Ok(new { Message = result });
        }
    }
}

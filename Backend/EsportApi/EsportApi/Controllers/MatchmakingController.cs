using Microsoft.AspNetCore.Mvc;
using EsportApi.Services;
using EsportApi.Models;
using MongoDB.Driver;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MatchmakingController : ControllerBase
    {
        private readonly IMatchmakingService _matchService;
        private readonly IMongoClient _mongoClient; // Samo za Seed metodu

        public MatchmakingController(IMatchmakingService matchService, IMongoClient mongoClient)
        {
            _matchService = matchService;
            _mongoClient = mongoClient;
        }

        [HttpPost("join")]
        public async Task<IActionResult> Join(string userId)
        {
            await _matchService.AddToQueue(userId);
            return Ok("Joined queue.");
        }

        [HttpGet("check-match")]
        public async Task<IActionResult> Check()
        {
            var match = await _matchService.TryMatch();
            return match != null ? Ok(match) : Ok("Still waiting for players...");
        }

        [HttpPost("report-win")]
        public async Task<IActionResult> Win(string userId)
        {
            await _matchService.AddWin(userId);
            return Ok("Win reported.");
        }

        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetLeaderboard(int count = 10)
        {
            var board = await _matchService.GetTopPlayers(count);
            return Ok(board);
        }

        // Pomocna metoda za testiranje
        [HttpPost("seed-users")]
        public async Task<IActionResult> SeedUsers(string username)
        {
            var collection = _mongoClient.GetDatabase("EsportDb").GetCollection<UserProfile>("UserProfiles");
            var newUser = new UserProfile
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Username = username
            };
            await collection.InsertOneAsync(newUser);
            return Ok(newUser);
        }
    }
}
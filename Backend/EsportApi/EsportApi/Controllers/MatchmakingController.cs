using Microsoft.AspNetCore.Mvc;
using EsportApi.Models;
using MongoDB.Driver;
using EsportApi.Services.Interfaces;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MatchmakingController : ControllerBase
    {
        private readonly IMatchmakingService _matchService;
        private readonly IMongoClient _mongoClient; 

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
            
            return match != null ? Ok(match) : Ok(new { Message = "Still waiting for players..." });
        }

        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetLeaderboard(int count = 10)
        {
            var board = await _matchService.GetTopPlayers(count);
            return Ok(board);
        }

        [HttpPost("seed-users")]
        public async Task<IActionResult> SeedUsers(string username)
        {
            var collection = _mongoClient.GetDatabase("EsportDb").GetCollection<UserProfile>("Users");
            var newUser = new UserProfile
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Username = username
            };
            await collection.InsertOneAsync(newUser);
            return Ok(newUser);
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
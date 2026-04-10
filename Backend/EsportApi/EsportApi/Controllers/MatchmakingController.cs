using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MatchmakingController : ControllerBase
    {
        private readonly IMatchmakingService _matchService;

        public MatchmakingController(IMatchmakingService matchService)
        {
            _matchService = matchService;
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
                    : Ok(new { message = "Still waiting for players..." });
            }

            var match = await _matchService.TryMatch();

            return match != null ? Ok(match) : Ok(new { message = "Still waiting for players..." });
        }

        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetLeaderboard(int count = 10)
        {
            var board = await _matchService.GetTopPlayers(count);
            return Ok(board);
        }

        [HttpPost("join-tournament")]
        public async Task<IActionResult> JoinTournament(string userId)
        {
            var result = await _matchService.JoinTournamentQueueAsync(userId);

            if (result.Contains("Nedovoljan") || result.Contains("ne postoji") || result.Contains("Vec si"))
                return BadRequest(new { message = result });

            return Ok(new { message = result });
        }
    }
}

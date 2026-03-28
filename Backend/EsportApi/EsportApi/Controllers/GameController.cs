using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GameController : ControllerBase
    {
        private readonly IGameService _gameService;
        private readonly IMatchmakingService _matchmakingService;

        public GameController(IGameService gameService, IMatchmakingService matchmakingService)
        {
            _gameService = gameService;
            _matchmakingService = matchmakingService;
        }

        [HttpPost("start")]
        public async Task<IActionResult> Start(string p1, string p2)
        {
            var matchId = await _gameService.StartGameAsync(p1, p2);
            return Ok(new { MatchId = matchId });
        }

        [HttpGet("{matchId}")]
        public async Task<IActionResult> GetStatus(string matchId)
        {
            var game = await _gameService.GetGameStateAsync(matchId);
            if (game == null) return NotFound("Meč nije u memoriji.");
            return Ok(game);
        }

        // U GameController.cs neka doda IMatchmakingService u konstruktor
        [HttpPost("move")]
        public async Task<IActionResult> Move(string matchId, string playerId, int position, string symbol, int version)
        {
            var result = await _gameService.MakeMoveAsync(matchId, playerId, position, symbol, version);

            if (result.StartsWith("Greska")) return BadRequest(new { Message = result });

            // Ako rezultat vraća "Kraj! Pobednik...", dodajemo pobedu u leaderboard!
            if (result.Contains("Pobednik"))
            {
                await _matchmakingService.AddWin(playerId);
            }

            return Ok(new { Message = result });
        }

        [HttpPost("snapshot-leaderboard")]
        public async Task<IActionResult> SnapshotLeaderboard()
        {
            await _gameService.SaveLeaderboardSnapshotAsync();
            return Ok(new { Message = "Dnevni presek Leaderboard-a uspesno arhiviran u Cassandru!" });
        }
    }
}
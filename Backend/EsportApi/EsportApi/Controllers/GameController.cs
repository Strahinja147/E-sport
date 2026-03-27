using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GameController : ControllerBase
    {
        private readonly IGameService _gameService;

        public GameController(IGameService gameService)
        {
            _gameService = gameService;
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

        [HttpPost("move")]
        public async Task<IActionResult> Move(string matchId, string playerId, int position, string symbol, int version)
        {
            var result = await _gameService.MakeMoveAsync(matchId, playerId, position, symbol, version);
            if (result.StartsWith("Greska")) return BadRequest(new { Message = result });
            return Ok(new { Message = result });
        }
    }
}
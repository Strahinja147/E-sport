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

        [HttpGet("{matchId}")]
        public async Task<IActionResult> GetStatus(string matchId)
        {
            var game = await _gameService.GetGameStateAsync(matchId);
            if (game == null) return NotFound("Meč nije u memoriji.");
            return Ok(game);
        }

        [HttpGet("{matchId}/move")]
        public async Task<IActionResult> GetMove(string matchId)
        {
            var game = await _gameService.GetMoveAsync(matchId);
            if (game == null) return NotFound("Meč nije u memoriji.");
            return Ok(game);
        }

        [HttpGet("history/{userId}")]
        public async Task<IActionResult> GetMatchHistory(string userId)
        {
            var history = await _gameService.GetMatchHistoryAsync(userId);
            return Ok(history);
        }

        [HttpGet("{matchId}/moves")]
        public async Task<IActionResult> GetMatchMoves(string matchId)
        {
            var moves = await _gameService.GetMatchMovesAsync(matchId);
            return Ok(moves);
        }

        [HttpGet("{matchId}/chat")]
        public async Task<IActionResult> GetMatchChat(string matchId)
        {
            var chat = await _gameService.GetChatHistoryAsync(matchId);
            return Ok(chat);
        }

        [HttpPost("move")]
        public async Task<IActionResult> Move(string matchId, string playerId, int position, string symbol, int version)
        {
            var result = await _gameService.MakeMoveAsync(matchId, playerId, position, symbol, version);

            if (result.StartsWith("Greska")) return BadRequest(new { Message = result });

            return Ok(new { Message = result });
        }

        [HttpPost("snapshot-leaderboard")]
        public async Task<IActionResult> SnapshotLeaderboard()
        {
            await _gameService.SaveLeaderboardSnapshotAsync();
            return Ok(new { Message = "Dnevni presek rang liste je uspesno sacuvan." });
        }


        [HttpPost("{matchId}/chat")]
        public async Task<IActionResult> SendChatMessage(string matchId, string playerId, string message)
        {
            var username = await _gameService.SaveChatMessageAsync(matchId, playerId, message);

            if (username == null)
                return BadRequest("ZABRANJENO: Nisi učesnik ovog meča!");

            return Ok($"Poruka igraca {username} je uspesno poslata.");
        }
    }
}

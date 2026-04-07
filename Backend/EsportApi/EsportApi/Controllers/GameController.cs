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
        public async Task<IActionResult> Start(string p1, string p2, string? matchId = null, string? tournamentId = null)
        {
            var newMatchId = await _gameService.StartGameAsync(p1, p2, matchId, tournamentId);
            return Ok(new { MatchId = newMatchId });
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

        // U GameController.cs neka doda IMatchmakingService u konstruktor
        [HttpPost("move")]
        public async Task<IActionResult> Move(string matchId, string playerId, int position, string symbol, int version)
        {
            // 1. Tvoj servis odradi sav posao (promeni tablu, proveri pobedu, azurira ELO u Mongo-u i telemetriju u Cassandri)
            var result = await _gameService.MakeMoveAsync(matchId, playerId, position, symbol, version);

            if (result.StartsWith("Greska")) return BadRequest(new { Message = result });

            // 2. Više nam ne treba AddWin ovde, jer je GameService već ažurirao bazu!
            return Ok(new { Message = result });
        }

        [HttpPost("snapshot-leaderboard")]
        public async Task<IActionResult> SnapshotLeaderboard()
        {
            await _gameService.SaveLeaderboardSnapshotAsync();
            return Ok(new { Message = "Dnevni presek Leaderboard-a uspesno arhiviran u Cassandru!" });
        }


        // SAMO ZA TESTIRANJE CHAT-A PREKO BACKA !!!!!!!!!!!!!!
        // U GameController.cs promeni SendChatMessage rutu:
        [HttpPost("{matchId}/chat")]
        public async Task<IActionResult> SendChatMessage(string matchId, string playerId, string message)
        {
            var username = await _gameService.SaveChatMessageAsync(matchId, playerId, message);

            // Ako je vratio null, znači da je Izbacivač proradio!
            if (username == null)
                return BadRequest("ZABRANJENO: Nisi učesnik ovog meča!");

            return Ok($"Poruka od igrača {username} sačuvana u Redis!");
        }
    }
}

using EsportApi.Models;
using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TournamentController : ControllerBase
    {
        private readonly ITournamentService _tournamentService;

        public TournamentController(ITournamentService tournamentService)
        {
            _tournamentService = tournamentService;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateTournament(string name)
        {
            var tournament = await _tournamentService.CreateTournament(name);
            return Ok(tournament);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTournament(string id)
        {
            var tournament = await _tournamentService.GetTournament(id);
            if (tournament == null) return NotFound("Turnir nije pronadjen.");
            return Ok(tournament);
        }

        [HttpPost("advance-winner")]
        public async Task<IActionResult> AdvanceWinner(string tournamentId, string matchId, string winnerId)
        {
            var success = await _tournamentService.AdvanceWinner(tournamentId, matchId, winnerId);
            if (success)
                return Ok("Transakcija uspela! Pobednik je prebačen u sledeću rundu.");
            else
                return BadRequest("Transakcija je propala i urađen je Rollback.");
        }

        [HttpPost("generate-bracket/{tournamentId}")]
        public async Task<IActionResult> GenerateBracket(string tournamentId, [FromBody] List<string> playerIds)
        {
            try
            {
                var success = await _tournamentService.GenerateBracket(tournamentId, playerIds);
                if (success)
                    return Ok("Zreb je uspesno napravljen i Runda 1 je pocela!");

                return BadRequest("Doslo je do greske pri pravljenju zreba.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("details/{id}")]
        public async Task<IActionResult> GetTournamentWithDetails(string id)
        {
            var tournamentDetails = await _tournamentService.GetTournamentWithDetails(id);
            if (tournamentDetails == null) return NotFound("Turnir nije pronadjen.");

            return Ok(tournamentDetails);
        }
    }
}

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

        [HttpGet]
        public async Task<IActionResult> GetAllTournaments()
        {
            return Ok(await _tournamentService.GetAllTournaments());
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

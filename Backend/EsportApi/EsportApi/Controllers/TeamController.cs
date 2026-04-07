using EsportApi.Services;
using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TeamController : ControllerBase
    {
        private readonly ITeamService _teamService;

        public TeamController(ITeamService teamService)
        {
            _teamService = teamService;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateTeam(string name, string ownerId)
        {
            try
            {
                var team = await _teamService.CreateTeam(name, ownerId);
                return Ok(team);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("add-member")]
        public async Task<IActionResult> AddMember(string teamId, string userId)
        {
            try
            {
                var success = await _teamService.AddMemberToTeam(teamId, userId);
                return success ? Ok("Član uspesno dodat u tim!") : BadRequest("Greška pri dodavanju.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("send-invite")]
        public async Task<IActionResult> SendInvite(string teamId, string senderId, string userId)
        {
            try
            {
                var result = await _teamService.SendInvite(teamId, senderId, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("send-invite-by-username")]
        public async Task<IActionResult> SendInviteByUsername(string teamId, string senderId, string username)
        {
            try
            {
                var result = await _teamService.SendInviteByUsername(teamId, senderId, username);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("accept-invite")]
        public async Task<IActionResult> AcceptInvite(string teamId, string userId)
        {
            try
            {
                var result = await _teamService.AcceptInvite(teamId, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("reject-invite")]
        public async Task<IActionResult> RejectInvite(string teamId, string userId)
        {
            try
            {
                var result = await _teamService.RejectInvite(teamId, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("cancel-invite")]
        public async Task<IActionResult> CancelInvite(string teamId, string senderId, string userId)
        {
            try
            {
                var result = await _teamService.CancelInvite(teamId, senderId, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{teamId}")]
        public async Task<IActionResult> GetTeam(string teamId)
        {
            var team = await _teamService.GetTeam(teamId);
            if (team == null) return NotFound("Tim nije pronadjen.");
            return Ok(team);
        }
    }
}

using EsportApi.Models;

namespace EsportApi.Services.Interfaces
{
    public interface ITeamService
    {
        Task<Team> CreateTeam(string name, string ownerId);
        Task<bool> AddMemberToTeam(string teamId, string userId);
        Task<string> SendInvite(string teamId, string senderId, string userId);
        Task<string> SendInviteByUsername(string teamId, string senderId, string username);
        Task<string> AcceptInvite(string teamId, string userId);
        Task<string> RejectInvite(string teamId, string userId);
        Task<string> CancelInvite(string teamId, string senderId, string userId);
        Task<Team?> GetTeam(string teamId);
        Task RecalculateTeamElo(string teamId);
    }
}

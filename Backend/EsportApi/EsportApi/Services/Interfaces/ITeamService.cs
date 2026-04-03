using EsportApi.Models;

namespace EsportApi.Services.Interfaces
{
    public interface ITeamService
    {
        Task<Team> CreateTeam(string name, string ownerId);
        Task<bool> AddMemberToTeam(string teamId, string userId);
        Task<Team?> GetTeam(string teamId);
    }
}

using EsportApi.Models;

namespace EsportApi.Services
{
    public interface ITournamentService
    {
        Task<Tournament> CreateTournament(string name);
        Task<Tournament?> GetTournament(string id);
        Task<bool> AdvanceWinner(string tournamentId, string matchId, string winnerId);
        Task<bool> GenerateBracket(string tournamentId, List<string> playerIds);
    }
}

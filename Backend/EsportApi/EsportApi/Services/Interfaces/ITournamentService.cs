using EsportApi.Models;
using EsportApi.Models.DTOs;

namespace EsportApi.Services.Interfaces
{
    public interface ITournamentService
    {
        Task<Tournament> CreateTournament(string name);
        Task<List<Tournament>> GetAllTournaments();
        Task<Tournament?> GetTournament(string id);
        Task<bool> AdvanceWinner(string tournamentId, string matchId, string winnerId);
        Task<bool> GenerateBracket(string tournamentId, List<string> playerIds);
        Task<TournamentDetailsDto?> GetTournamentWithDetails(string tournamentId);
    }
}

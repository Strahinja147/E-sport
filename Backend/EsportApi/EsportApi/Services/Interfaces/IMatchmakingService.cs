using EsportApi.Models;

namespace EsportApi.Services.Interfaces
{
    public class MatchFoundDto
    {
        public string MatchId { get; set; }
        public string Player1 { get; set; }
        public string Player2 { get; set; }
        public string Player1Id { get; set; }
        public string Player2Id { get; set; }
    }
    public interface IMatchmakingService
    {
        Task AddToQueue(string userId);
        Task<MatchFoundDto?> TryMatch();
        Task<MatchFoundDto?> GetAssignedMatchAsync(string userId);

        Task<List<LeaderboardEntry>> GetTopPlayers(int count);
        Task<string> JoinTournamentQueueAsync(string userId);
        Task<List<string>?> CheckTournamentQueueAsync(int requiredPlayers);
    }
}

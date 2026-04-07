using EsportApi.Models;

namespace EsportApi.Services.Interfaces
{

    // Mali DTO za povratnu informaciju
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
        // Matchmaking deo (Redis Lists)
        Task AddToQueue(string userId);
        Task<MatchFoundDto?> TryMatch(); // <-- Promenjeno ovde
        Task<MatchFoundDto?> GetAssignedMatchAsync(string userId);

        // Leaderboard deo (Redis Sorted Sets + MongoDB)
        Task<List<LeaderboardEntry>> GetTopPlayers(int count);
        Task<string> JoinTournamentQueueAsync(string userId);
        Task<List<string>?> CheckTournamentQueueAsync(int requiredPlayers);
    }
}

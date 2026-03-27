using EsportApi.Models;

namespace EsportApi.Services.Interfaces
{

    // Mali DTO za povratnu informaciju
    public class MatchFoundDto
    {
        public string MatchId { get; set; }
        public string Player1 { get; set; }
        public string Player2 { get; set; }
    }
    public interface IMatchmakingService
    {
        // Matchmaking deo (Redis Lists)
        Task AddToQueue(string userId);
        Task<MatchFoundDto?> TryMatch(); // <-- Promenjeno ovde

        // Leaderboard deo (Redis Sorted Sets + MongoDB)
        Task AddWin(string userId);
        Task<List<LeaderboardEntry>> GetTopPlayers(int count);
    }
}
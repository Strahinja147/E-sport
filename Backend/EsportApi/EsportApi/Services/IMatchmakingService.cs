using EsportApi.Models;

namespace EsportApi.Services
{
    public interface IMatchmakingService
    {
        // Matchmaking deo (Redis Lists)
        Task AddToQueue(string userId);
        Task<string?> TryMatch();

        // Leaderboard deo (Redis Sorted Sets + MongoDB)
        Task AddWin(string userId);
        Task<List<LeaderboardEntry>> GetTopPlayers(int count);
    }
}
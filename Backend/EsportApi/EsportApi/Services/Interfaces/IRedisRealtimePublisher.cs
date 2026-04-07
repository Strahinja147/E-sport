using EsportApi.Models;
using EsportApi.Services.Interfaces;

namespace EsportApi.Services.Interfaces
{
    public interface IRedisRealtimePublisher
    {
        Task PublishMoveAsync(string matchId, TicTacToeGame game);
        Task PublishGameFinishedAsync(string matchId, string resultText, string board);
        Task PublishChatAsync(string matchId, string username, string message);
        Task PublishMatchFoundAsync(MatchFoundDto match);
        Task PublishTournamentStartedAsync(string tournamentId, List<string> playerIds);
    }
}

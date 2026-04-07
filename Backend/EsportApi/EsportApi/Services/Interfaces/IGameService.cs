using EsportApi.Models;
using EsportApi.Models.DTOs;

public interface IGameService
{
    Task<string> StartGameAsync(string player1Id, string player2Id, string? matchId = null, string? tournamentId = null);
    Task<TicTacToeGame> GetGameStateAsync(string matchId);
    Task<TicTacToeGame> GetMoveAsync(string matchId);
    Task<List<MatchHistoryItemDto>> GetMatchHistoryAsync(string userId);
    Task<List<MatchMoveDto>> GetMatchMovesAsync(string matchId);
    Task<string> MakeMoveAsync(string matchId, string playerId, int position, string symbol, int clientVersion);
    Task<List<PlayerProgress>> GetPlayerProgressAsync(string userId);
    Task SaveLeaderboardSnapshotAsync();
    Task<string> SaveChatMessageAsync(string matchId, string playerId, string message);
    Task<List<string>> GetChatHistoryAsync(string matchId);
}

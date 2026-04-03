using EsportApi.Models;

public interface IGameService
{
    Task<string> StartGameAsync(string player1Id, string player2Id, string? matchId = null, string? tournamentId = null);
    Task<TicTacToeGame> GetGameStateAsync(string matchId);
    // Dodat parametar clientVersion
    Task<string> MakeMoveAsync(string matchId, string playerId, int position, string symbol, int clientVersion);
    Task<List<PlayerProgress>> GetPlayerProgressAsync(string userId);
    Task SaveLeaderboardSnapshotAsync();

    // ==========================================
    // NOVO: REDIS MATCH CHAT
    // ==========================================
    // U IGameService.cs promeni potpis metode:
    Task<string> SaveChatMessageAsync(string matchId, string playerId, string message);
    Task<List<string>> GetChatHistoryAsync(string matchId);
}
using EsportApi.Models;

public interface IGameService
{
    Task<string> StartGameAsync(string player1Id, string player2Id);
    Task<TicTacToeGame> GetGameStateAsync(string matchId);
    // Dodat parametar clientVersion
    Task<string> MakeMoveAsync(string matchId, string playerId, int position, string symbol, int clientVersion);
}
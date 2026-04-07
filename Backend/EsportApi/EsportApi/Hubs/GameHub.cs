using Microsoft.AspNetCore.SignalR;
using EsportApi.Services.Interfaces;

namespace EsportApi.Hubs
{
    public class GameHub : Hub
    {
        private readonly IGameService _gameService;

        // Ubacujemo tvoj servis u Hub
        public GameHub(IGameService gameService)
        {
            _gameService = gameService;
        }

        public async Task JoinMatch(string matchId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, matchId);
        }

        // U GameHub.cs promeni metodu SendMessage:
        public async Task SendMessage(string matchId, string playerId, string message)
        {
            // Ako nije učesnik, SaveChatMessageAsync će vratiti null i ništa se neće desiti.
            // Validna poruka se sada dalje salje preko Redis pub/sub subscriber-a.
            _ = await _gameService.SaveChatMessageAsync(matchId, playerId, message);
        }
    }
}

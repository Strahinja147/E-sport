using Microsoft.AspNetCore.SignalR;
using EsportApi.Services.Interfaces;

namespace EsportApi.Hubs
{
    public class GameHub : Hub
    {
        private readonly IGameService _gameService;

        public GameHub(IGameService gameService)
        {
            _gameService = gameService;
        }

        public async Task JoinMatch(string matchId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, matchId);
        }

        public async Task SendMessage(string matchId, string playerId, string message)
        {
            _ = await _gameService.SaveChatMessageAsync(matchId, playerId, message);
        }
    }
}

using Microsoft.AspNetCore.SignalR;

namespace EsportApi.Hubs
{
    public class GameHub : Hub
    {
        // Igrač poziva ovo sa frontenda da bi ušao u "sobu" svog meča
        public async Task JoinMatch(string matchId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, matchId);
        }

        // Opciono: Možeš dodati metodu ako želiš chat ili nešto slično
        public async Task SendMessage(string matchId, string user, string message)
        {
            await Clients.Group(matchId).SendAsync("ReceiveMessage", user, message);
        }
    }
}
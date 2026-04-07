using System.Text.Json;
using EsportApi.Models;
using EsportApi.Services.Interfaces;
using StackExchange.Redis;

namespace EsportApi.Services
{
    public sealed class RedisRealtimePublisher : IRedisRealtimePublisher
    {
        public const string ChannelName = "realtime:game-events";

        private readonly IConnectionMultiplexer _redis;

        public RedisRealtimePublisher(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public Task PublishMoveAsync(string matchId, TicTacToeGame game) =>
            PublishAsync(new RedisRealtimeEvent
            {
                Type = "move",
                MatchId = matchId,
                Game = game
            });

        public Task PublishGameFinishedAsync(string matchId, string resultText, string board) =>
            PublishAsync(new RedisRealtimeEvent
            {
                Type = "finished",
                MatchId = matchId,
                ResultText = resultText,
                Board = board
            });

        public Task PublishChatAsync(string matchId, string username, string message) =>
            PublishAsync(new RedisRealtimeEvent
            {
                Type = "chat",
                MatchId = matchId,
                Username = username,
                Message = message
            });

        public Task PublishMatchFoundAsync(MatchFoundDto match) =>
            PublishAsync(new RedisRealtimeEvent
            {
                Type = "match-found",
                MatchId = match.MatchId,
                Player1 = match.Player1,
                Player2 = match.Player2,
                Player1Id = match.Player1Id,
                Player2Id = match.Player2Id
            });

        public Task PublishTournamentStartedAsync(string tournamentId, List<string> playerIds) =>
            PublishAsync(new RedisRealtimeEvent
            {
                Type = "tournament-started",
                TournamentId = tournamentId,
                PlayerIds = playerIds
            });

        private async Task PublishAsync(RedisRealtimeEvent realtimeEvent)
        {
            var subscriber = _redis.GetSubscriber();
            var payload = JsonSerializer.Serialize(realtimeEvent);
            await subscriber.PublishAsync(RedisChannel.Literal(ChannelName), payload);
        }
    }
}

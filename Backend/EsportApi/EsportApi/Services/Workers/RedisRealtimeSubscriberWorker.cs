using System.Text.Json;
using EsportApi.Hubs;
using EsportApi.Models;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace EsportApi.Services.Workers
{
    public sealed class RedisRealtimeSubscriberWorker : BackgroundService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IHubContext<GameHub> _hubContext;
        private readonly ILogger<RedisRealtimeSubscriberWorker> _logger;
        private ChannelMessageQueue? _subscription;

        public RedisRealtimeSubscriberWorker(
            IConnectionMultiplexer redis,
            IHubContext<GameHub> hubContext,
            ILogger<RedisRealtimeSubscriberWorker> logger)
        {
            _redis = redis;
            _hubContext = hubContext;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var subscriber = _redis.GetSubscriber();
            _subscription = await subscriber.SubscribeAsync(RedisChannel.Literal(RedisRealtimePublisher.ChannelName));

            _subscription.OnMessage(async channelMessage =>
            {
                try
                {
                    var realtimeEvent = JsonSerializer.Deserialize<RedisRealtimeEvent>(channelMessage.Message!);
                    if (realtimeEvent == null || string.IsNullOrWhiteSpace(realtimeEvent.MatchId))
                    {
                        return;
                    }

                    switch (realtimeEvent.Type)
                    {
                        case "move" when realtimeEvent.Game != null:
                            await _hubContext.Clients.Group(realtimeEvent.MatchId).SendAsync(
                                "ReceiveMove",
                                realtimeEvent.Game);
                            break;

                        case "finished":
                            await _hubContext.Clients.Group(realtimeEvent.MatchId).SendAsync(
                                "GameFinished",
                                realtimeEvent.ResultText,
                                realtimeEvent.Board);
                            break;

                        case "chat":
                            await _hubContext.Clients.Group(realtimeEvent.MatchId).SendAsync(
                                "ReceiveMessage",
                                realtimeEvent.Username,
                                realtimeEvent.Message);
                            break;

                        case "match-found":
                            await _hubContext.Clients.All.SendAsync(
                                "MatchFound",
                                new
                                {
                                    MatchId = realtimeEvent.MatchId,
                                    Player1 = realtimeEvent.Player1,
                                    Player2 = realtimeEvent.Player2,
                                    Player1Id = realtimeEvent.Player1Id,
                                    Player2Id = realtimeEvent.Player2Id
                                });
                            break;

                        case "tournament-started":
                            await _hubContext.Clients.All.SendAsync(
                                "TournamentStarted",
                                realtimeEvent.TournamentId,
                                realtimeEvent.PlayerIds);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Redis realtime event processing failed.");
                }
            });

            using var registration = stoppingToken.Register(() =>
            {
                if (_subscription != null)
                {
                    _subscription.Unsubscribe();
                }
            });

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

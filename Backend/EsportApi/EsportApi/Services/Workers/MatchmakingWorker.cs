using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using EsportApi.Services.Interfaces;

namespace EsportApi.Services.Workers
{
    public class MatchmakingWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MatchmakingWorker> _logger;

        public MatchmakingWorker(IServiceScopeFactory scopeFactory, ILogger<MatchmakingWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=> Matchmaking Background Worker je POKRENUT!");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var matchService = scope.ServiceProvider.GetRequiredService<IMatchmakingService>();
                        var realtimePublisher = scope.ServiceProvider.GetRequiredService<IRedisRealtimePublisher>();

                        var match = await matchService.TryMatch();

                        if (match != null)
                        {
                            _logger.LogInformation($"[MATCHMAKING SUCCESS] Spojeni: {match.Player1} i {match.Player2} u meč: {match.MatchId}");

                            await realtimePublisher.PublishMatchFoundAsync(match);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[MATCHMAKING ERROR] {ex.Message}");
                }

                await Task.Delay(3000, stoppingToken);
            }
        }
    }
}

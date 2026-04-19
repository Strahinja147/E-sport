using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using EsportApi.Services.Interfaces;

namespace EsportApi.Services.Workers
{
    public class LeaderboardSnapshotWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LeaderboardSnapshotWorker> _logger;

        public LeaderboardSnapshotWorker(IServiceScopeFactory scopeFactory, ILogger<LeaderboardSnapshotWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=> Leaderboard Snapshot Worker je POKRENUT! (Čuva presek na svakih sat vremena)");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();

                        await gameService.SaveLeaderboardSnapshotAsync();

                        _logger.LogInformation($"[{DateTime.UtcNow:HH:mm:ss}] [CASSANDRA] Leaderboard snapshot uspešno arhiviran!");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Greška pri čuvanju snapshot-a: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
    }
}

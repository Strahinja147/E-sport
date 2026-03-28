using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using EsportApi.Hubs; // Tvoj Hub
using EsportApi.Services.Interfaces; // Tvoji interfejsi

namespace EsportApi.Services
{
    public class MatchmakingWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MatchmakingWorker> _logger;

        // Ubacujemo tvoj ScopeFactory i Logger
        public MatchmakingWorker(IServiceScopeFactory scopeFactory, ILogger<MatchmakingWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=> Matchmaking Background Worker je POKRENUT!");

            // Beskonačna petlja koja radi dokle god je upaljen server
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Tvoj genijalni Scope pristup
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        // Izvlačimo Matchmaking servis
                        var matchService = scope.ServiceProvider.GetRequiredService<IMatchmakingService>();

                        // Izvlačimo SignalR Hub da bismo javili frontendu
                        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

                        // Sistem SAM poziva funkciju
                        var match = await matchService.TryMatch();

                        if (match != null)
                        {
                            // Uspesno upareni!
                            _logger.LogInformation($"[MATCHMAKING SUCCESS] Spojeni: {match.Player1} i {match.Player2} u meč: {match.MatchId}");

                            // ODMAH ispaljujemo poruku na frontend preko SignalR-a!
                            // Frontend React aplikacija će slušati event "MatchFound"
                            await hubContext.Clients.All.SendAsync("MatchFound", match);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[MATCHMAKING ERROR] {ex.Message}");
                }

                // Tvojih 3 sekunde (odličan tajming za Redis)
                await Task.Delay(3000, stoppingToken);
            }
        }
    }
}
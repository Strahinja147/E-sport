using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EsportApi.Services.Interfaces;

namespace EsportApi.Services.Workers
{
    public class TournamentWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TournamentWorker> _logger;

        public TournamentWorker(IServiceScopeFactory scopeFactory, ILogger<TournamentWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=> Tournament Worker je POKRENUT! (Čeka igrače...)");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var matchService = scope.ServiceProvider.GetRequiredService<IMatchmakingService>();
                    var tourService = scope.ServiceProvider.GetRequiredService<ITournamentService>();
                    var realtimePublisher = scope.ServiceProvider.GetRequiredService<IRedisRealtimePublisher>();

                    // ZA TESTIRANJE: Tražimo 4 igrača (Polufinale -> Finale)
                    // U realnosti bi ovde stajalo 8 ili 16
                    var readyPlayers = await matchService.CheckTournamentQueueAsync(4);

                    if (readyPlayers != null)
                    {
                        _logger.LogInformation($"[TOURNAMENT] Skupilo se 4 igrača! Pravim turnir...");

                        // 1. Kreiramo turnir u MongoDB
                        var tournamentName = "Pro Kup " + DateTime.UtcNow.ToString("HH:mm");
                        var tournament = await tourService.CreateTournament(tournamentName);

                        // 2. Generišemo žreb (Runda 1 kreće)
                        await tourService.GenerateBracket(tournament.Id, readyPlayers);

                        _logger.LogInformation($"[TOURNAMENT STARTED] ID: {tournament.Id}");

                        await realtimePublisher.PublishTournamentStartedAsync(tournament.Id, readyPlayers);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[TOURNAMENT ERROR]: {ex.Message}");
                }

                await Task.Delay(4000, stoppingToken); // Proverava svake 4 sekunde
            }
        }
    }
}

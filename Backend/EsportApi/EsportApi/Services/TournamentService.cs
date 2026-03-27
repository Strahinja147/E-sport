using EsportApi.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using StackExchange.Redis;
using System.Text.Json;

namespace EsportApi.Services
{
    public class TournamentService : ITournamentService
    {
        private readonly IMongoClient _mongoClient;
        private readonly IMongoCollection<Tournament> _tournamentsCollection;
        private readonly IMongoCollection<Match> _matchesCollection; // Dodali smo i mečeve!
        private readonly IDatabase _redisDb;

        public TournamentService(IMongoClient mongoClient, IConnectionMultiplexer redis)
        {
            _mongoClient = mongoClient;
            var db = _mongoClient.GetDatabase("EsportDb");

            // Povezujemo se na dve kolekcije da bi radili transakciju nad obe
            _tournamentsCollection = db.GetCollection<Tournament>("Tournaments");
            _matchesCollection = db.GetCollection<Match>("Matches");

            _redisDb = redis.GetDatabase();
        }

        public async Task<Tournament> CreateTournament(string name)
        {
            var tournament = new Tournament
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = name,
                Rounds = new List<TournamentRound>()
            };

            await _tournamentsCollection.InsertOneAsync(tournament);
            return tournament;
        }

        public async Task<Tournament?> GetTournament(string id)
        {
            string cacheKey = $"tournament:{id}";

            // 1. Proveri da li turnir već postoji u Redisu
            var cachedTournament = await _redisDb.StringGetAsync(cacheKey);

            if (!cachedTournament.IsNullOrEmpty)
            {
                // Ako postoji u Redisu, vraćamo ga odmah (Uštedeli smo upit ka Mongu!)
                Console.WriteLine("Turnir ucitan iz REDIS KESA!");
                return JsonSerializer.Deserialize<Tournament>(cachedTournament.ToString());
            }

            // 2. Ako nije u Redisu, čitamo ga iz MongoDB-a
            var tournament = await _tournamentsCollection.Find(t => t.Id == id).FirstOrDefaultAsync();

            if (tournament != null)
            {
                // 3. Upisujemo ga u Redis da bi sledeći put bio tu (čuvamo ga 5 minuta)
                var json = JsonSerializer.Serialize(tournament);
                await _redisDb.StringSetAsync(cacheKey, json, TimeSpan.FromMinutes(5));
                Console.WriteLine("Turnir ucitan iz MONGO Baze i sacuvan u KES!");
            }

            return tournament;
        }

        // --- MAGIJA TRANSAKCIJE POČINJE OVDE ---
        public async Task<bool> AdvanceWinner(string tournamentId, string matchId, string winnerId)
        {
            using var session = await _mongoClient.StartSessionAsync();
            session.StartTransaction();
            try
            {
                // 1. Zapiši ko je pobedio u trenutnom meču
                var matchFilter = Builders<Match>.Filter.Eq(m => m.Id, matchId);
                var matchUpdate = Builders<Match>.Update.Set(m => m.WinnerId, winnerId).Set(m => m.Status, "Finished");
                await _matchesCollection.UpdateOneAsync(session, matchFilter, matchUpdate);

                // 2. Pronađi turnir da vidimo u kojoj smo rundi
                var tourFilter = Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId);
                var tournament = await _tournamentsCollection.Find(session, tourFilter).FirstOrDefaultAsync();

                if (tournament == null) throw new Exception("Turnir ne postoji!");

                // Nalazimo u kojoj rundi se nalazio ovaj meč
                var currentRound = tournament.Rounds.FirstOrDefault(r => r.MatchIds.Contains(matchId));
                if (currentRound == null) throw new Exception("Meč ne pripada ovom turniru!");

                // 3. DA LI JE OVO FINALE? (Ako runda ima samo 1 meč, to je finale)
                if (currentRound.MatchIds.Count == 1)
                {
                    // KRAJ TURNIRA! Ne pravimo nove mečeve. 
                    // Ovde možeš dodati u MongoDB da je ceo Tournament.Status = "Completed"
                    Console.WriteLine($"\n*** KRAJ TURNIRA! APSOLUTNI POBEDNIK JE: {winnerId} ***\n");
                    tournament.Status = "Completed";

                    await session.CommitTransactionAsync();
                    await _redisDb.KeyDeleteAsync($"tournament:{tournamentId}");
                    return true;
                }

                // 4. NIJE FINALE -> Idemo u sledeću rundu
                int nextRoundNumber = currentRound.RoundNumber + 1;
                var nextRound = tournament.Rounds.FirstOrDefault(r => r.RoundNumber == nextRoundNumber);

                bool tournamentUpdated = false; // Prati da li moramo da snimimo promenu u turniru

                if (nextRound == null)
                {
                    // Sledeća runda još ne postoji, Pera je prvi koji je prošao dalje!
                    var newMatch = new Match
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        Player1Id = winnerId,
                        Player2Id = "TBD", // Čeka protivnika
                        Status = "Pending"
                    };
                    await _matchesCollection.InsertOneAsync(session, newMatch);

                    tournament.Rounds.Add(new TournamentRound
                    {
                        RoundNumber = nextRoundNumber,
                        MatchIds = new List<string> { newMatch.Id }
                    });
                    tournamentUpdated = true;
                }
                else
                {
                    // Sledeća runda postoji! Da li neko već čeka protivnika?
                    // Vadimo sve mečeve iz sledeće runde da nađemo onaj sa "TBD"
                    var nextRoundMatches = await _matchesCollection
                        .Find(session, Builders<Match>.Filter.In(m => m.Id, nextRound.MatchIds))
                        .ToListAsync();

                    var waitingMatch = nextRoundMatches.FirstOrDefault(m => m.Player2Id == "TBD");

                    if (waitingMatch != null)
                    {
                        // Našli smo meč gde neko čeka! Pera ulazi kao Player 2.
                        var updateWaitingMatch = Builders<Match>.Update.Set(m => m.Player2Id, winnerId);
                        await _matchesCollection.UpdateOneAsync(session, m => m.Id == waitingMatch.Id, updateWaitingMatch);
                    }
                    else
                    {
                        // Svi postojeći mečevi u sledećoj rundi su već puni. Pravimo novi!
                        var newMatch = new Match
                        {
                            Id = ObjectId.GenerateNewId().ToString(),
                            Player1Id = winnerId,
                            Player2Id = "TBD",
                            Status = "Pending"
                        };
                        await _matchesCollection.InsertOneAsync(session, newMatch);
                        nextRound.MatchIds.Add(newMatch.Id);
                        tournamentUpdated = true;
                    }
                }

                // 5. Ako smo menjali sam dokument turnira (dodali rundu ili ID meča), sačuvaj ga
                if (tournamentUpdated)
                {
                    await _tournamentsCollection.ReplaceOneAsync(session, tourFilter, tournament);
                }

                // Sve je prošlo super!
                await session.CommitTransactionAsync();
                await _redisDb.KeyDeleteAsync($"tournament:{tournamentId}"); // Brišemo keš
                return true;
            }
            catch (Exception ex)
            {
                await session.AbortTransactionAsync();
                Console.WriteLine($"Transakcija propala: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> GenerateBracket(string tournamentId, List<string> playerIds)
        {
            // Prosta provera: Da li imamo paran broj igrača? (Za pravi Iks-Oks turnir obično ide 4, 8, 16...)
            if (playerIds.Count < 2 || playerIds.Count % 2 != 0)
            {
                throw new Exception("Broj igrača mora biti paran (npr. 2, 4, 8)!");
            }

            using var session = await _mongoClient.StartSessionAsync();
            session.StartTransaction();
            try
            {
                var matchIdsForRound1 = new List<string>();

                // 1. Spajamo igrače po dvoje i pravimo mečeve
                for (int i = 0; i < playerIds.Count; i += 2)
                {
                    var newMatch = new Match
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        Player1Id = playerIds[i],
                        Player2Id = playerIds[i + 1],
                        Status = "Pending" // Meč tek treba da se igra
                    };

                    await _matchesCollection.InsertOneAsync(session, newMatch);
                    matchIdsForRound1.Add(newMatch.Id);
                }

                // 2. Ažuriramo Turnir (Pravimo "Rundu 1")
                var tourFilter = Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId);
                var tournament = await _tournamentsCollection.Find(session, tourFilter).FirstOrDefaultAsync();

                if (tournament != null)
                {
                    tournament.Rounds.Add(new TournamentRound
                    {
                        RoundNumber = 1,
                        MatchIds = matchIdsForRound1
                    });
                    tournament.Status = "On Going";

                    await _tournamentsCollection.ReplaceOneAsync(session, tourFilter, tournament);
                }

                // 3. Commit transakcije
                await session.CommitTransactionAsync();

                // 4. OBAVEZNO: Brišemo keš iz Redisa jer se turnir promenio!
                await _redisDb.KeyDeleteAsync($"tournament:{tournamentId}");

                return true;
            }
            catch (Exception ex)
            {
                await session.AbortTransactionAsync();
                Console.WriteLine($"Greska pri pravljenju zreba: {ex.Message}");
                return false;
            }
        }
    }
}

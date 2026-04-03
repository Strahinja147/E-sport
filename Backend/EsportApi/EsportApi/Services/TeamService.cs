using EsportApi.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using EsportApi.Services.Interfaces;

namespace EsportApi.Services
{
    public class TeamService : ITeamService
    {
        private readonly IMongoCollection<Team> _teamsCollection;
        private readonly IMongoCollection<UserProfile> _usersCollection;

        public TeamService(IMongoClient mongoClient)
        {
            var db = mongoClient.GetDatabase("EsportDb");
            _teamsCollection = db.GetCollection<Team>("Teams");
            _usersCollection = db.GetCollection<UserProfile>("Users"); // Treba nam da proverimo da li korisnik postoji i da mu izvučemo ELO
        }

        public async Task<Team> CreateTeam(string name, string ownerId)
        {
            // Provera da li Owner uopšte postoji u bazi
            var owner = await _usersCollection.Find(u => u.Id == ownerId).FirstOrDefaultAsync();
            if (owner == null) throw new Exception("Korisnik koji pravi tim ne postoji!");

            var team = new Team
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = name,
                OwnerId = ownerId,
                MemberIds = new List<string> { ownerId }, // Vlasnik je prvi član
                TeamElo = owner.EloRating, // Za početak ELO tima je jednak ELO-u vlasnika
                TeamAchievements = new List<string> { "Tim osnovan!" }
            };

            await _teamsCollection.InsertOneAsync(team);
            var userUpdate = Builders<UserProfile>.Update.Set(u => u.CurrentTeamId, team.Id);
            await _usersCollection.UpdateOneAsync(u => u.Id == ownerId, userUpdate);

            return team;
        }

        public async Task<bool> AddMemberToTeam(string teamId, string userId)
        {
            // 1. Proveravamo da li tim postoji
            var team = await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
            if (team == null) throw new Exception("Tim ne postoji!");

            // 2. Provera da li je korisnik već u timu
            if (team.MemberIds.Contains(userId)) throw new Exception("Korisnik je već u ovom timu!");

            // 3. Proveravamo da li novi korisnik postoji
            var newUser = await _usersCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (newUser == null) throw new Exception("Korisnik ne postoji!");

            newUser.CurrentTeamId = teamId; 
            // 4. RAČUNANJE NOVOG ELO PROSEKA
            // Pravimo listu svih ID-jeva (stari članovi + ovaj novi što tek ulazi)
            var allMemberIds = new List<string>(team.MemberIds) { userId };

            // Vadimo sve te korisnike iz baze ODJEDNOM
            var allMembers = await _usersCollection.Find(u => allMemberIds.Contains(u.Id)).ToListAsync();

            // Računamo prosečan ELO (Average) i zaokružujemo na ceo broj (int)
            int newTeamElo = (int)allMembers.Average(u => u.EloRating);

            // 5. MONGODB ATOMSKI UPDATE
            // U jednoj operaciji guramo novog člana u listu I menjamo TeamElo!
            var update = Builders<Team>.Update
                .Push(t => t.MemberIds, userId)
                .Set(t => t.TeamElo, newTeamElo);

            var result = await _teamsCollection.UpdateOneAsync(t => t.Id == teamId, update);
            var userUpdate = Builders<UserProfile>.Update.Set(u => u.CurrentTeamId, teamId);
            await _usersCollection.UpdateOneAsync(u => u.Id == userId, userUpdate);

            return result.ModifiedCount > 0;
        }

        public async Task<Team?> GetTeam(string teamId)
        {
            return await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
        }

        public async Task RecalculateTeamElo(string teamId)
        {
            // 1. Nađemo tim
            var team = await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
            if (team == null || team.MemberIds.Count == 0) return;

            // 2. Povučemo sve članove tima (njihove najnovije ELO rejtinge)
            var allMembers = await _usersCollection.Find(u => team.MemberIds.Contains(u.Id)).ToListAsync();

            // 3. Računamo novi prosek
            int newTeamElo = (int)allMembers.Average(u => u.EloRating);

            // 4. Ažuriramo samo ELO u timu
            var update = Builders<Team>.Update.Set(t => t.TeamElo, newTeamElo);
            await _teamsCollection.UpdateOneAsync(t => t.Id == teamId, update);
        }
    }
}
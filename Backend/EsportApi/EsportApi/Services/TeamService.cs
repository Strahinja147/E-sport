using EsportApi.Models;
using EsportApi.Services.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

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
            _usersCollection = db.GetCollection<UserProfile>("Users");
        }

        public async Task<Team> CreateTeam(string name, string ownerId)
        {
            var owner = await _usersCollection.Find(u => u.Id == ownerId).FirstOrDefaultAsync();
            if (owner == null) throw new Exception("Korisnik koji pravi tim ne postoji!");
            if (!string.IsNullOrWhiteSpace(owner.CurrentTeamId)) throw new Exception("Vec si clan nekog tima.");

            var team = new Team
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = name,
                OwnerId = ownerId,
                MemberIds = new List<string> { ownerId },
                TeamElo = owner.EloRating,
                TeamAchievements = new List<string> { "Tim osnovan!" }
            };

            await _teamsCollection.InsertOneAsync(team);
            var userUpdate = Builders<UserProfile>.Update.Set(u => u.CurrentTeamId, team.Id);
            await _usersCollection.UpdateOneAsync(u => u.Id == ownerId, userUpdate);

            return team;
        }

        public async Task<bool> AddMemberToTeam(string teamId, string userId)
        {
            var team = await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
            if (team == null) throw new Exception("Tim ne postoji!");
            if (team.MemberIds.Contains(userId)) throw new Exception("Korisnik je vec u ovom timu!");

            var newUser = await _usersCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (newUser == null) throw new Exception("Korisnik ne postoji!");
            if (!string.IsNullOrWhiteSpace(newUser.CurrentTeamId) && newUser.CurrentTeamId != teamId)
            {
                throw new Exception("Korisnik je vec clan drugog tima.");
            }

            var allMemberIds = new List<string>(team.MemberIds) { userId };
            var allMembers = await _usersCollection.Find(u => allMemberIds.Contains(u.Id)).ToListAsync();
            int newTeamElo = (int)allMembers.Average(u => u.EloRating);

            var update = Builders<Team>.Update
                .Push(t => t.MemberIds, userId)
                .Set(t => t.TeamElo, newTeamElo)
                .PullFilter(t => t.PendingInvites, invite => invite.UserId == userId);

            var result = await _teamsCollection.UpdateOneAsync(t => t.Id == teamId, update);

            var userUpdate = Builders<UserProfile>.Update
                .Set(u => u.CurrentTeamId, teamId)
                .Set(u => u.TeamInvites, new List<TeamInvite>());
            await _usersCollection.UpdateOneAsync(u => u.Id == userId, userUpdate);

            await RemoveInviteFromAllTeams(userId);
            return result.ModifiedCount > 0;
        }

        public async Task<string> SendInvite(string teamId, string senderId, string userId)
        {
            var team = await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
            if (team == null) throw new Exception("Tim ne postoji!");
            if (!team.MemberIds.Contains(senderId)) throw new Exception("Samo clanovi tima mogu da salju pozive.");
            if (userId == senderId) throw new Exception("Ne mozes da posaljes poziv samom sebi.");
            if (team.MemberIds.Contains(userId)) throw new Exception("Taj korisnik je vec u timu.");
            if (team.PendingInvites.Any(invite => invite.UserId == userId)) throw new Exception("Poziv za tog korisnika je vec poslat.");

            var sender = await _usersCollection.Find(u => u.Id == senderId).FirstOrDefaultAsync();
            var receiver = await _usersCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (sender == null || receiver == null) throw new Exception("Korisnik nije pronadjen.");
            if (!string.IsNullOrWhiteSpace(receiver.CurrentTeamId)) throw new Exception("Korisnik je vec clan nekog tima.");
            if (receiver.TeamInvites.Any(invite => invite.TeamId == teamId)) throw new Exception("Korisnik vec ima aktivan poziv za ovaj tim.");

            var requestedAt = DateTime.UtcNow;
            var pendingInvite = new TeamPendingInvite
            {
                UserId = receiver.Id,
                Username = receiver.Username,
                RequestedByUserId = sender.Id,
                RequestedByUsername = sender.Username,
                RequestedAt = requestedAt
            };

            var userInvite = new TeamInvite
            {
                TeamId = team.Id,
                TeamName = team.Name,
                RequestedByUserId = sender.Id,
                RequestedByUsername = sender.Username,
                RequestedAt = requestedAt
            };

            await _teamsCollection.UpdateOneAsync(
                t => t.Id == teamId,
                Builders<Team>.Update.Push(t => t.PendingInvites, pendingInvite));

            await _usersCollection.UpdateOneAsync(
                u => u.Id == userId,
                Builders<UserProfile>.Update.Push(u => u.TeamInvites, userInvite));

            return $"Poziv za tim {team.Name} je poslat korisniku {receiver.Username}.";
        }

        public async Task<string> SendInviteByUsername(string teamId, string senderId, string username)
        {
            var normalizedUsername = username.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername)) throw new Exception("Unesi korisnicko ime.");

            var allUsers = await _usersCollection.Find(_ => true).ToListAsync();
            var receiver = allUsers.FirstOrDefault(
                candidate => string.Equals(candidate.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
            if (receiver == null) throw new Exception("Korisnik sa tim korisnickim imenom nije pronadjen.");

            return await SendInvite(teamId, senderId, receiver.Id);
        }

        public async Task<string> AcceptInvite(string teamId, string userId)
        {
            var user = await _usersCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (user == null) throw new Exception("Korisnik nije pronadjen.");
            if (!user.TeamInvites.Any(invite => invite.TeamId == teamId)) throw new Exception("Poziv za taj tim nije pronadjen.");

            var team = await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
            if (team == null) throw new Exception("Tim vise ne postoji.");

            await AddMemberToTeam(teamId, userId);
            return $"Uspešno si prihvatio poziv za tim {team.Name}.";
        }

        public async Task<string> RejectInvite(string teamId, string userId)
        {
            var team = await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
            if (team == null) throw new Exception("Tim vise ne postoji.");

            await RemoveInvite(teamId, userId);
            return $"Poziv za tim {team.Name} je odbijen.";
        }

        public async Task<string> CancelInvite(string teamId, string senderId, string userId)
        {
            var team = await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
            if (team == null) throw new Exception("Tim vise ne postoji.");
            if (!team.MemberIds.Contains(senderId)) throw new Exception("Samo clan tima moze da opozove poziv.");

            var invite = team.PendingInvites.FirstOrDefault(pendingInvite => pendingInvite.UserId == userId);
            if (invite == null) throw new Exception("Aktivan poziv za tog korisnika ne postoji.");
            if (invite.RequestedByUserId != senderId && team.OwnerId != senderId)
            {
                throw new Exception("Samo posiljalac ili vlasnik tima mogu da opozovu poziv.");
            }

            await RemoveInvite(teamId, userId);
            return "Poziv za tim je opozvan.";
        }

        public async Task<Team?> GetTeam(string teamId)
        {
            return await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
        }

        public async Task RecalculateTeamElo(string teamId)
        {
            var team = await _teamsCollection.Find(t => t.Id == teamId).FirstOrDefaultAsync();
            if (team == null || team.MemberIds.Count == 0) return;

            var allMembers = await _usersCollection.Find(u => team.MemberIds.Contains(u.Id)).ToListAsync();
            int newTeamElo = (int)allMembers.Average(u => u.EloRating);

            var update = Builders<Team>.Update.Set(t => t.TeamElo, newTeamElo);
            await _teamsCollection.UpdateOneAsync(t => t.Id == teamId, update);
        }

        private async Task RemoveInvite(string teamId, string userId)
        {
            await _teamsCollection.UpdateOneAsync(
                t => t.Id == teamId,
                Builders<Team>.Update.PullFilter(t => t.PendingInvites, invite => invite.UserId == userId));

            await _usersCollection.UpdateOneAsync(
                u => u.Id == userId,
                Builders<UserProfile>.Update.PullFilter(u => u.TeamInvites, invite => invite.TeamId == teamId));
        }

        private async Task RemoveInviteFromAllTeams(string userId)
        {
            await _teamsCollection.UpdateManyAsync(
                Builders<Team>.Filter.Empty,
                Builders<Team>.Update.PullFilter(t => t.PendingInvites, invite => invite.UserId == userId));
        }
    }
}

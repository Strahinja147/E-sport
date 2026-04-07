using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EsportApi.Models
{
    public class Team
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string OwnerId { get; set; } // Onaj ko je napravio tim
        public List<string> MemberIds { get; set; } = new(); // Svi članovi (uključujući i vlasnika)
        public List<TeamPendingInvite> PendingInvites { get; set; } = new();
        public int TeamElo { get; set; } // Početni prosečni ELO
        public List<string> TeamAchievements { get; set; } = new(); // Npr. "Osvajač Pro Kupa 2026"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TeamPendingInvite
    {
        public required string UserId { get; set; }
        public required string Username { get; set; }
        public required string RequestedByUserId { get; set; }
        public required string RequestedByUsername { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }
}

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EsportApi.Models
{
    public class Match
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public required string Id { get; set; }
        public string? TournamentId { get; set; }
        public required string Player1Id { get; set; }
        public required string Player2Id { get; set; }
        public string? WinnerId { get; set; } // ? znači da može biti null dok meč traje
        public string Status { get; set; } = "InProgress";
        public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    }
}
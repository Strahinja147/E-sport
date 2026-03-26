using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EsportApi.Models
{
    public class Tournament
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public required string Id { get; set; }
        public required string Name { get; set; }
        public List<TournamentRound> Rounds { get; set; } = new();
    }

    public class TournamentRound
    {
        public int RoundNumber { get; set; }
        public List<string> MatchIds { get; set; } = new();
    }
}
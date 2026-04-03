using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EsportApi.Models
{
    public class ShopItem
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public required string Id { get; set; }
        public required string Name { get; set; }
        public int Price { get; set; }

        // NOVO:
        public bool IsLimited { get; set; }
        public int InitialStock { get; set; } // Koliko ih je bilo na početku
        public int CurrentStock { get; set; }
    }
}
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

public class UserProfile
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }
    public required string Username { get; set; }
    public int EloRating { get; set; } = 1000;
    public int Coins { get; set; } = 0;
    public PlayerStatistics Stats { get; set; } = new();

    /* 
       ZAŠTO: 
       1. Ugnježdeni Stats: Umesto da PlayerStatistics bude posebna kolekcija, 
          ugnjezdili smo je u UserProfile.
       2. Razlog: Kad god neko gleda profil, želi da vidi i statistiku. 
          U MongoDB-u je jedan "JOIN" (lookup) skuplji nego čitanje jednog dokumenta.
    */
}

public class PlayerStatistics
{
    public int TotalGames { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    // Ovako nateramo Mongo da vidi i upiše WinRate
    [BsonElement("WinRate")]
    public double WinRate
    {
        get => TotalGames > 0 ? Math.Round((double)Wins / TotalGames, 2) : 0;
        set { /* Prazan set jer se polje samo računa */ }

    }
    public DateTime LastGameAt { get; set; }
}
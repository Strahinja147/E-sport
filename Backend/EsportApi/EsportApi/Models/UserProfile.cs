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

    public List<Friend> Friends { get; set; } = new();
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
    public int TournamentsPlayed { get; set; }
    public int TournamentWins { get; set; }
    public double TournamentWinRate { get; set; }

    // Ovako nateramo Mongo da vidi i upiše WinRate
    //[BsonElement("WinRate")]
    public double WinRate { get; set; }
    public DateTime LastGameAt { get; set; }
}

// NOVA KLASA ZA UGNJEŽDENI OBJEKAT PRIJATELJA
public class Friend
{
    public required string UserId { get; set; }
    public required string Username { get; set; }
    public string Status { get; set; } = "Pending"; // Može biti "Pending" ili "Accepted"
}
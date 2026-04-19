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

    public string? CurrentTeamId { get; set; }
    public List<Friend> Friends { get; set; } = new();
    public List<TeamInvite> TeamInvites { get; set; } = new();
}

public class PlayerStatistics
{
    public int TotalGames { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int TournamentsPlayed { get; set; }
    public int TournamentWins { get; set; }
    public double TournamentWinRate { get; set; }
    public double WinRate { get; set; }
    public DateTime LastGameAt { get; set; }
}

public class Friend
{
    public required string UserId { get; set; }
    public required string Username { get; set; }
    public string Status { get; set; } = "Pending";
    public string? RequestedByUserId { get; set; }
}

public class TeamInvite
{
    public required string TeamId { get; set; }
    public required string TeamName { get; set; }
    public required string RequestedByUserId { get; set; }
    public required string RequestedByUsername { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

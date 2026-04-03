namespace EsportApi.Models
{
    public class LeaderboardEntry
    {
        public int Rank { get; set; }          // 1, 2, 3...
        public required string UserId { get; set; }
        public required string Username { get; set; } // Zbog Frontenda
        public int EloRating { get; set; }     // Glavni kriterijum!
        public int Wins { get; set; }
        public int TournamentWins { get; set; }
    }
}

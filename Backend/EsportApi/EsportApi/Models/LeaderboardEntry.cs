namespace EsportApi.Models
{
    public class LeaderboardEntry
    {
        public required string UserId { get; set; }
        public required string Username { get; set; }
        public int Wins { get; set; } // Ovo je score u Redis Sorted Set-u
    }
}

namespace EsportApi.Models.DTOs
{
    public class MatchHistoryItemDto
    {
        public required string MatchId { get; set; }
        public required string OpponentName { get; set; }
        public required string Result { get; set; }
        public required string Symbol { get; set; }
        public required DateTime PlayedAt { get; set; }
        public bool IsTournament { get; set; }
        public string? TournamentName { get; set; }
    }

    public class MatchMoveDto
    {
        public int MoveNumber { get; set; }
        public required string PlayerName { get; set; }
        public required string Symbol { get; set; }
        public int Position { get; set; }
        public required DateTime MovedAt { get; set; }
    }
}

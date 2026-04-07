namespace EsportApi.Models
{
    public sealed class RedisRealtimeEvent
    {
        public required string Type { get; set; }
        public string? MatchId { get; set; }
        public string? Player1 { get; set; }
        public string? Player2 { get; set; }
        public string? Player1Id { get; set; }
        public string? Player2Id { get; set; }
        public TicTacToeGame? Game { get; set; }
        public string? TournamentId { get; set; }
        public List<string>? PlayerIds { get; set; }
        public string? ResultText { get; set; }
        public string? Board { get; set; }
        public string? Username { get; set; }
        public string? Message { get; set; }
    }
}

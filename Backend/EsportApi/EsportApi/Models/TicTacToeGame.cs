namespace EsportApi.Models
{
    public class TicTacToeGame
    {
        public required string Id { get; set; }
        public required string Board { get; set; } = "_________";
        public required string CurrentTurn { get; set; }
        public int Version { get; set; }
        public string Status { get; set; } = "InProgress";
        public string? Player1Id { get; set; }
        public string? Player2Id { get; set; }
        public string? TournamentId { get; set; }
    }
}

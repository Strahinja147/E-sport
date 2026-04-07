namespace EsportApi.Models
{
    public class TicTacToeGame
    {
        public required string Id { get; set; } // MatchId
        public required string Board { get; set; } = "_________"; // 9 karaktera
        public required string CurrentTurn { get; set; } // "X" ili "O"
        public int Version { get; set; } // Inkrementira se pri svakom potezu
        public string Status { get; set; } = "InProgress"; // "InProgress", "Draw", "Won"
        public string? Player1Id { get; set; }
        public string? Player2Id { get; set; }

        public string? TournamentId { get; set; } // DODATO: Ako ovo nije null, znači da se igra turnir!
        /* 
       ZAŠTO: 
       1. Version: Sprečava "race condition". Ako dva poteza stignu istovremeno, 
          prihvatamo samo onaj koji odgovara verziji "N+1".
        */
    }
}

namespace EsportApi.Models
{
    public class TicTacToeGame
    {
        public required string Id { get; set; } // MatchId
        public required string Board { get; set; } = "_________"; // 9 karaktera
        public required string CurrentTurn { get; set; } // "X" ili "O"
        public int Version { get; set; } // Inkrementira se pri svakom potezu
        public string Status { get; set; } = "InProgress"; // "InProgress", "Draw", "Won"
        /* 
       ZAŠTO: 
       1. Version: Sprečava "race condition". Ako dva poteza stignu istovremeno, 
          prihvatamo samo onaj koji odgovara verziji "N+1".
        */
    }
}

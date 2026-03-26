namespace EsportApi.Models
{
    public class TicTacToeMove
    {
        public required string MatchId { get; set; } // Particioni ključ za pretragu meča
        public DateTime Timestamp { get; set; } // Clustering ključ za redosled
        public required string PlayerId { get; set; }
        public int Position { get; set; } // 0-8
        public required string Symbol { get; set; } // "X" ili "O"
    }
}

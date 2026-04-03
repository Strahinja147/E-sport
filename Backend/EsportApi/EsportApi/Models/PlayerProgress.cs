namespace EsportApi.Models
{
    public class PlayerProgress
    {
        public required string UserId { get; set; }
        public DateTime Timestamp { get; set; }
        public int Elo { get; set; }
        public int Coins { get; set; }
        public required string ChangeReason { get; set; }
    }
}
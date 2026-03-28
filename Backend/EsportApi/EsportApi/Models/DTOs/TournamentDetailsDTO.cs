namespace EsportApi.Models.DTOs
{
    public class TournamentDetailsDto
    {
        public required string Id { get; set; }
        public string Name { get; set; }
        public List<TournamentRoundDto> Rounds { get; set; } = new();
    }

    public class TournamentRoundDto
    {
        public int RoundNumber { get; set; }
        public List<MatchDetailsDto> Matches { get; set; } = new();
    }

    public class MatchDetailsDto
    {
        public required string Id { get; set; }
        public string Status { get; set; }
        public PlayerDto Player1 { get; set; }
        public PlayerDto Player2 { get; set; }
        public PlayerDto Winner { get; set; }
    }

    public class PlayerDto
    {
        public required string Id { get; set; }
        public string Username { get; set; }
    }
}

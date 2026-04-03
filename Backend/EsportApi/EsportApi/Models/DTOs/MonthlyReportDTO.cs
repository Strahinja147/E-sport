namespace EsportApi.Models.DTOs
{
    public class MonthlyReportDto
    {
        public required string Month { get; set; }
        public int TotalRevenue { get; set; }
        public required string BestSellingItem { get; set; }
        public int SalesCount { get; set; }
    }
}
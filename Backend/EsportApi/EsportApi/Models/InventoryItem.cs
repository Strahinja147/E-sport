namespace EsportApi.Models
{
    public class InventoryItem
    {
        // Za Cassandru se obično koristi Guid, ali možeš i string ako ti je lakše
        public required string UserId { get; set; }
        public required string ItemId { get; set; }
        public required string ItemName { get; set; }
        public DateTime PurchasedAt { get; set; }
    }
}
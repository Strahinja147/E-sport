namespace EsportApi.Models
{
    public class InventoryItem
    {
        public required string UserId { get; set; }
        public required string ItemId { get; set; }
        public required string ItemName { get; set; }
        public DateTime PurchasedAt { get; set; }
    }
}

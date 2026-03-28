namespace EsportApi.Models.DTOs
{
    public class InventoryItemDTO
    {
        public required string ItemId { get; set; }
        public required string ItemName { get; set; }
        public DateTime PurchasedAt { get; set; }
    }
}

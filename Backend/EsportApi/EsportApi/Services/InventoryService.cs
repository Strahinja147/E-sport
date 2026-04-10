using Cassandra;
using EsportApi.Models;
using EsportApi.Models.DTOs;
using EsportApi.Services.Interfaces;
using MongoDB.Driver;

namespace EsportApi.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly Cassandra.ISession _cassandraSession;
        private readonly IMongoCollection<ShopItem> _shopItems;

        public InventoryService(Cassandra.ISession cassandraSession, IMongoClient mongoClient)
        {
            _cassandraSession = cassandraSession;
            _shopItems = mongoClient.GetDatabase("EsportDb").GetCollection<ShopItem>("ShopItems");
        }

        public async Task<List<InventoryItemDTO>> GetInventoryByUserIdAsync(string userId)
        {
            var inventory = await ReadInventoryAsync(
                "SELECT item_id, item_name, purchased_at, purchase_price FROM esports.inventory_by_user WHERE user_id = ?",
                userId,
                hasPurchasePrice: true);

            return await EnrichInventoryAsync(inventory);
        }

        private async Task<List<InventoryItemDTO>> ReadInventoryAsync(string query, string userId, bool hasPurchasePrice)
        {
            var prepared = await _cassandraSession.PrepareAsync(query);
            var resultSet = await _cassandraSession.ExecuteAsync(prepared.Bind(userId));
            var inventory = new List<InventoryItemDTO>();

            foreach (var row in resultSet)
            {
                var purchasePrice = hasPurchasePrice && !row.IsNull("purchase_price")
                    ? row.GetValue<int>("purchase_price")
                    : 0;

                inventory.Add(new InventoryItemDTO
                {
                    ItemId = row.GetValue<string>("item_id"),
                    ItemName = row.GetValue<string>("item_name"),
                    PurchasedAt = row.GetValue<DateTimeOffset>("purchased_at").UtcDateTime,
                    PurchasePrice = purchasePrice,
                    ResalePrice = CalculateResalePrice(purchasePrice)
                });
            }

            return inventory;
        }

        private async Task<List<InventoryItemDTO>> EnrichInventoryAsync(List<InventoryItemDTO> inventory)
        {
            if (inventory.Count == 0)
            {
                return inventory;
            }

            var missingPriceIds = inventory
                .Where(item => item.PurchasePrice <= 0)
                .Select(item => item.ItemId)
                .Distinct()
                .ToList();

            var fallbackPriceMap = new Dictionary<string, int>();

            if (missingPriceIds.Count > 0)
            {
                var shopItems = await _shopItems
                    .Find(Builders<ShopItem>.Filter.In(item => item.Id, missingPriceIds))
                    .ToListAsync();

                fallbackPriceMap = shopItems.ToDictionary(item => item.Id, item => item.Price);
            }

            foreach (var item in inventory)
            {
                if (item.PurchasePrice <= 0 && fallbackPriceMap.TryGetValue(item.ItemId, out var fallbackPrice))
                {
                    item.PurchasePrice = fallbackPrice;
                }

                item.ResalePrice = CalculateResalePrice(item.PurchasePrice);
            }

            return inventory;
        }

        private static int CalculateResalePrice(int purchasePrice)
        {
            if (purchasePrice <= 0)
            {
                return 0;
            }

            return (int)Math.Floor(purchasePrice * 0.9m);
        }
    }
}

using Cassandra;
using EsportApi.Models;
using EsportApi.Models.DTOs;
using EsportApi.Services.Interfaces;

namespace EsportApi.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly Cassandra.ISession _cassandraSession;

        public InventoryService(Cassandra.ISession cassandraSession)
        {
            _cassandraSession = cassandraSession;
        }

        public async Task<List<InventoryItemDTO>> GetInventoryByUserIdAsync(string userId)
        {
            var inventory = new List<InventoryItemDTO>();
            var query = "SELECT item_id, item_name, purchased_at FROM esports.inventory WHERE user_id = ?";
            var prepared = await _cassandraSession.PrepareAsync(query);
            var resultSet = await _cassandraSession.ExecuteAsync(prepared.Bind(userId));

            foreach (var row in resultSet)
            {
                inventory.Add(new InventoryItemDTO
                {
                    ItemId = row.GetValue<string>("item_id"), // Čitamo kao string
                    ItemName = row.GetValue<string>("item_name"),
                    PurchasedAt = row.GetValue<DateTimeOffset>("purchased_at").DateTime
                });
            }
            return inventory;
        }
        public async Task<bool> HasItemAsync(string userId, string itemId)
        {
            // Sada direktno bind-ujemo string, nema konverzije!
            var query = "SELECT item_id FROM esports.inventory WHERE user_id = ? AND item_id = ? ALLOW FILTERING";
            var prepared = await _cassandraSession.PrepareAsync(query);
            var resultSet = await _cassandraSession.ExecuteAsync(prepared.Bind(userId, itemId));

            return resultSet.FirstOrDefault() != null;
        }
    }
}
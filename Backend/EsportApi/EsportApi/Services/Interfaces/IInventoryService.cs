using EsportApi.Models;
using EsportApi.Models.DTOs;

namespace EsportApi.Services.Interfaces
{
    public interface IInventoryService
    {
        Task<List<InventoryItemDTO>> GetInventoryByUserIdAsync(string userId);
    }
}

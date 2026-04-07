using EsportApi.Models;
using EsportApi.Models.DTOs;

namespace EsportApi.Services.Interfaces
{
    public interface IShopService
    {
        Task<string> BuyItemAsync(string userId, string itemId);
        Task<string> SellItemAsync(string userId, string itemId, DateTime purchasedAt);
        Task<int> GetMonthlyRevenueAsync(string yearMonth);
        Task AddCoinsAsync(string userId, int amount);
        Task<MonthlyReportDto> GetMonthlyRevenueReportAsync(string yearMonth);
        Task<List<ShopItem>> GetAllItemsAsync();
    }
}

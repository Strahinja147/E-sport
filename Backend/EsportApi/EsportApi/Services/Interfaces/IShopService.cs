using EsportApi.Models;
using EsportApi.Models.DTOs;

namespace EsportApi.Services.Interfaces
{
    public interface IShopService
    {
        // Igrač kupuje predmet, vraća string sa porukom o statusu
        Task<string> BuyItemAsync(string userId, string itemId);

        Task<int> GetMonthlyRevenueAsync(string yearMonth);
        // Potrebno za testiranje da dodaš pare igraču
        Task AddCoinsAsync(string userId, int amount);
        Task<MonthlyReportDto> GetMonthlyRevenueReportAsync(string yearMonth);
        Task<List<ShopItem>> GetAllItemsAsync();
    }
}

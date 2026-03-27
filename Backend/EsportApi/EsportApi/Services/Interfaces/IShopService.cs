using EsportApi.Models;

namespace EsportApi.Services.Interfaces
{
    public interface IShopService
    {
        // Igrač kupuje predmet, vraća string sa porukom o statusu
        Task<string> BuyItemAsync(string userId, string itemId);

        // Potrebno za testiranje da dodaš pare igraču
        Task AddCoinsAsync(string userId, int amount);
        Task<List<ShopItem>> GetAllItemsAsync();
    }
}

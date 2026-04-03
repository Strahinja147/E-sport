using EsportApi.Models;
using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShopController : ControllerBase
    {
        private readonly IShopService _shopService;
        private readonly IMongoClient _mongoClient;
        public ShopController(IShopService shopService, IMongoClient mongoClient)
        {
            _shopService = shopService;
            _mongoClient = mongoClient;
        }

        [HttpPost("buy")]
        public async Task<IActionResult> Buy(string userId, string itemId)
        {
            var result = await _shopService.BuyItemAsync(userId, itemId);

            // Popravka: Proveravamo da li rezultat sadrži potvrdu uspeha, bez obzira na tačan tekst
            if (result.Contains("USPEŠNO") || result.Contains("Uspešna"))
            {
                return Ok(new { Message = result });
            }

            // Ako nije uspeh, onda je greška (nema para, nema na stanju, itd.)
            return BadRequest(new { Message = result });
        }

        [HttpGet("revenue/{yearMonth}")] // Format: 2026-03
        public async Task<IActionResult> GetRevenue(string yearMonth)
        {
            var report = await _shopService.GetMonthlyRevenueReportAsync(yearMonth);

            if (report.TotalRevenue == 0)
                return NotFound($"Nema podataka za mesec {yearMonth}");

            return Ok(report);
        }

        [HttpPost("add-coins")]
        public async Task<IActionResult> AddCoins(string userId, int amount)
        {
            if (amount <= 0) return BadRequest("Mora biti > 0");
            await _shopService.AddCoinsAsync(userId, amount);
            return Ok();
        }

        [HttpGet("items")]
        public async Task<IActionResult> GetAllItems()
        {
            return Ok(await _shopService.GetAllItemsAsync());
        }
        [HttpPost("seed-limited-item")]
        public async Task<IActionResult> SeedLimited()
        {
            var item = new ShopItem
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Name = "Zlatni X (Limited Edition)",
                Price = 1000,
                IsLimited = true,
                InitialStock = 5, // Samo 5 komada ikada!
                CurrentStock = 5
            };

            var db = _mongoClient.GetDatabase("EsportDb").GetCollection<ShopItem>("ShopItems");
            await db.InsertOneAsync(item);
            return Ok(item);
        }
    }
}
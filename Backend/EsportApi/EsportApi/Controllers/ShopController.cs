using Microsoft.AspNetCore.Mvc;
using EsportApi.Services.Interfaces;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShopController : ControllerBase
    {
        private readonly IShopService _shopService;

        public ShopController(IShopService shopService)
        {
            _shopService = shopService;
        }

        [HttpPost("buy")]
        public async Task<IActionResult> Buy(string userId, string itemId)
        {
            var result = await _shopService.BuyItemAsync(userId, itemId);
            return result == "Uspešna kupovina!" ? Ok(new { Message = result }) : BadRequest(new { Message = result });
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
    }
}
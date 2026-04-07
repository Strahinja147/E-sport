using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

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

            if (result.Contains("USPEŠNO") || result.Contains("Uspešna"))
            {
                return Ok(new { Message = result });
            }

            return BadRequest(new { Message = result });
        }

        [HttpDelete("sell")]
        public async Task<IActionResult> Sell(string userId, string itemId, DateTime purchasedAt)
        {
            var result = await _shopService.SellItemAsync(userId, itemId, purchasedAt);

            if (result.Contains("USPEŠNO") || result.Contains("Uspešna"))
            {
                return Ok(new { Message = result });
            }

            return BadRequest(new { Message = result });
        }

        [HttpGet("revenue/{yearMonth}")]
        public async Task<IActionResult> GetRevenue(string yearMonth)
        {
            var report = await _shopService.GetMonthlyRevenueReportAsync(yearMonth);

            if (report.TotalRevenue == 0)
            {
                return NotFound($"Nema podataka za mesec {yearMonth}");
            }

            return Ok(report);
        }

        [HttpGet("items")]
        public async Task<IActionResult> GetAllItems()
        {
            return Ok(await _shopService.GetAllItemsAsync());
        }
    }
}

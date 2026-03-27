using Microsoft.AspNetCore.Mvc;
using EsportApi.Services.Interfaces;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ShopController : ControllerBase
    {
        private readonly IShopService _shopService;

        public ShopController(IShopService shopService)
        {
            _shopService = shopService;
        }

        // POST: /Shop/buy?userId=69c58182...&itemId=550e8400-e29b-41d4-a716-446655440000
        [HttpPost("buy")]
        public async Task<IActionResult> Buy(string userId, string itemId)
        {
            // Pozivamo servis koji smo implementirali
            var result = await _shopService.BuyItemAsync(userId, itemId);

            if (result == "Uspešna kupovina!")
                return Ok(new { Message = result });

            return BadRequest(new { Message = result });
        }

        // POST: /Shop/add-coins?userId=69c58182...&amount=500
        [HttpPost("add-coins")]
        public async Task<IActionResult> AddCoins(string userId, int amount)
        {
            if (amount <= 0) return BadRequest("Količina novčića mora biti veća od 0!");
            await _shopService.AddCoinsAsync(userId, amount);
            return Ok();
        }

        [HttpGet("items")]
        public async Task<IActionResult> GetAllItems()
        {
            var items = await _shopService.GetAllItemsAsync();
            return Ok(items);
        }
    }
}
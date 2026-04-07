using EsportApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet("my-inventory/{userId}")]
    public async Task<IActionResult> GetInventory(string userId)
    {
        // 1. Pozovi servis
        var items = await _inventoryService.GetInventoryByUserIdAsync(userId);

        // 2. Proveri da li je lista prazna ili null
        if (items == null) return NotFound("Inventar nije pronađen.");

        // 3. Vrati rezultate
        return Ok(items);
    }
}

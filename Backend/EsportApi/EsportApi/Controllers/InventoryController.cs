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

    // Ovo će Član 3 (Game Logic) koristiti da proveri da li igrač poseduje skin pre početka meča
    [HttpGet("has-item/{userId}/{itemId}")]
    public async Task<IActionResult> HasItem(string userId, string itemId)
    {
        var exists = await _inventoryService.HasItemAsync(userId, itemId);

        // Vraćamo jednostavan boolean odgovor
        return Ok(new { HasItem = exists });
    }
}
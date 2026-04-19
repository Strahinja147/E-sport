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
        var items = await _inventoryService.GetInventoryByUserIdAsync(userId);
        if (items == null) return NotFound("Inventar nije pronađen.");
        return Ok(items);
    }
}

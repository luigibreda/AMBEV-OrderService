using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.DTOs;
using OrderService.Models;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _context;

    public OrdersController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] OrderRequest request)
    {
        // Verifica se pedido já existe para evitar duplicação
        if (await _context.Orders.AnyAsync(o => o.ExternalId == request.ExternalId))
            return Conflict("Pedido já existe.");

        var order = new Order
        {
            ExternalId = request.ExternalId,
            Status = "RECEIVED",
            CreatedAt = DateTime.UtcNow,
            Products = request.Products.Select(p => new Product
            {
                Name = p.Name,
                Quantity = p.Quantity,
                UnitPrice = p.UnitPrice
            }).ToList()
        };

        // Calcula total
        order.TotalValue = order.Products.Sum(p => p.Total);

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetOrder), new { externalId = order.ExternalId }, new { order.ExternalId, order.TotalValue, order.Status, order.CreatedAt });
    }

    [HttpGet("{externalId}")]
    public async Task<IActionResult> GetOrder(string externalId)
    {
        var order = await _context.Orders
            .Include(o => o.Products)
            .FirstOrDefaultAsync(o => o.ExternalId == externalId);

        if (order == null)
            return NotFound();

        var response = new OrderResponse
        {
            ExternalId = order.ExternalId,
            TotalValue = order.TotalValue,
            Status = order.Status,
            CreatedAt = order.CreatedAt,
            Products = order.Products.Select(p => new ProductResponse
            {
                Name = p.Name,
                Quantity = p.Quantity,
                UnitPrice = p.UnitPrice,
                Total = p.Total
            }).ToList()
        };

        return Ok(response);
    }
}

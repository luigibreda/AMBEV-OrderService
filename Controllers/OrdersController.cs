using AMBEV_OrderService.Enums;
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
            Status = OrderStatus.RECEIVED,  
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
            Status = order.Status.ToString(),
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

    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] OrderQueryParams query)
    {
        var queryable = _context.Orders
            .Include(o => o.Products)
            .AsQueryable();

        if (!string.IsNullOrEmpty(query.Status))
        {
            if (Enum.TryParse<OrderStatus>(query.Status, true, out var status))
            {
                queryable = queryable.Where(o => o.Status == status);
            }
            else
            {
                return BadRequest("Status inválido.");
            }
        }

        if (query.StartDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(query.StartDate.Value, DateTimeKind.Utc);
            queryable = queryable.Where(o => o.CreatedAt >= startUtc);
        }

        if (query.EndDate.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(
                query.EndDate.Value.Date.AddDays(1).AddTicks(-1), // 23:59:59.9999999
                DateTimeKind.Utc
            );

            queryable = queryable.Where(o => o.CreatedAt <= endUtc);
        }

        var totalItems = await queryable.CountAsync();

        var orders = await queryable
            .OrderByDescending(o => o.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        var response = orders.Select(order => new OrderResponse
        {
            ExternalId = order.ExternalId,
            TotalValue = order.TotalValue,
            Status = order.Status.ToString(),
            CreatedAt = order.CreatedAt,
            Products = order.Products.Select(p => new ProductResponse
            {
                Name = p.Name,
                Quantity = p.Quantity,
                UnitPrice = p.UnitPrice,
                Total = p.Total
            }).ToList()
        });

        return Ok(new
        {
            totalItems,
            query.Page,
            query.PageSize,
            data = response
        });
    }

}

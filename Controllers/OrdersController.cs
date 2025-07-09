using System.Text;
using System.Text.Json;
using AMBEV_OrderService.Enums;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.DTOs;
using OrderService.Models;
using RabbitMQ.Client;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ConnectionFactory _connectionFactory;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(AppDbContext context, ConnectionFactory connectionFactory, ILogger<OrdersController> logger)
    {
        _context = context;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult CreateOrder([FromBody] OrderRequest request)
    {
        // Validação básica do payload
        if (request == null || string.IsNullOrWhiteSpace(request.ExternalId))
        {
            return BadRequest("Payload do pedido é inválido.");
        }
        
        try
        {
            // A controller agora delega o trabalho pesado. Ela só publica na fila.
            using var connection = _connectionFactory.CreateConnection();
            using var channel = connection.CreateModel();

            const string queueName = "orders";
            // A declaração da fila aqui garante que ela existe.
            // É importante que os parâmetros (durable, arguments) sejam os mesmos do consumidor.
            channel.QueueDeclare(queue: queueName,
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: new Dictionary<string, object>
                                 {
                                     { "x-dead-letter-exchange", "orders.dlx" }
                                 });

            var json = JsonSerializer.Serialize(request);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true; // Torna a mensagem durável

            channel.BasicPublish(exchange: "",
                                 routingKey: queueName,
                                 basicProperties: properties,
                                 body: body);

            _logger.LogInformation("Pedido com ExternalId {ExternalId} recebido e publicado na fila.", request.ExternalId);

            // Resposta 202 Accepted: A solicitação foi aceita para processamento, mas o processamento não foi concluído.
            // Esta é a resposta HTTP correta para um fluxo assíncrono.
            return Accepted(value: new { message = $"Pedido {request.ExternalId} recebido e enfileirado para processamento." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao publicar pedido {ExternalId} na fila.", request.ExternalId);
            return StatusCode(500, "Ocorreu um erro interno ao tentar processar o pedido.");
        }
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

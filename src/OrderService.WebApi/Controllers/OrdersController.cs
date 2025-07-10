using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.DTOs;
using OrderService.Application.Commands;
using OrderService.Application.Queries;
using OrderService.Domain.Models;
using OrderService.Infrastructure;
using OrderService.Infrastructure.Queries;
using RabbitMQ.Client;

namespace OrderService.WebApi.Controllers;

[ApiController]
[Route("[controller]")]

public class OrdersController : ControllerBase
{
    private readonly CreateOrderCommandHandler _createOrderHandler;
    private readonly GetOrderByIdQueryHandler _getOrderByIdHandler;
    private readonly AppDbContext _context;
    private readonly ILogger<OrdersController> _logger;
    private readonly IConnectionFactory _connectionFactory;
    private readonly Random _random = new Random();

    public OrdersController(
        CreateOrderCommandHandler createOrderHandler,
        GetOrderByIdQueryHandler getOrderByIdHandler,
        AppDbContext context,
        ILogger<OrdersController> logger,
        IConnectionFactory connectionFactory)
    {
        _createOrderHandler = createOrderHandler;
        _getOrderByIdHandler = getOrderByIdHandler;
        _context = context;
        _logger = logger;
        _connectionFactory = connectionFactory;
    }

    [HttpPost]
    public IActionResult CreateOrder([FromBody] OrderRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ExternalId))
        {
            return BadRequest("Payload do pedido é inválido.");
        }

        try
        {
            var command = new CreateOrderCommand
            {
                ExternalId = request.ExternalId,
                Products = request.Products
            };
            _createOrderHandler.Handle(command);
            return Accepted(new { message = $"Pedido {request.ExternalId} recebido e enfileirado para processamento." });
        }
        catch (Exception)
        {
            return StatusCode(500, "Ocorreu um erro interno ao tentar processar o pedido.");
        }
    }

    [HttpGet("{externalId}")]
    public async Task<IActionResult> GetOrder(string externalId)
    {
        var query = new GetOrderByIdQuery { ExternalId = externalId };
        var result = await _getOrderByIdHandler.Handle(query);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] OrderQueryParams query)
    {
        var queryable = _context.Set<Order>()
            .Include(o => o.Products)
            .AsQueryable();

        // Filtro de status pode ser ajustado conforme enum e domínio
        // if (!string.IsNullOrEmpty(query.Status))
        // {
        //     if (Enum.TryParse<OrderStatus>(query.Status, true, out var status))
        //     {
        //         queryable = queryable.Where(o => o.Status == status);
        //     }
        //     else
        //     {
        //         return BadRequest("Status inválido.");
        //     }
        // }

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

    [HttpPost("generate-test-orders")] 
    public IActionResult GenerateTestOrders([FromBody] GenerateOrdersRequest request)
    {
        if (request.Count <= 0 || request.Count > 1000000) // Limite razoável para evitar abuso
        {
            return BadRequest("O número de pedidos deve estar entre 1 e 1.000.000.");
        }

        // Esta é uma operação de longa duração, então executamos em uma Thread separada
        // para não bloquear a requisição HTTP.
        _ = Task.Run(async () =>
        {
            _logger.LogInformation("Iniciando geração de {Count} pedidos de teste.", request.Count);

            try
            {
                // Reutiliza a conexão e o canal para todas as publicações na carga
                // Isso é mais eficiente do que criar e fechar para cada mensagem.
                using var connection = _connectionFactory.CreateConnection();
                using var channel = connection.CreateModel();

                const string queueName = "orders";
                channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false,
                                     arguments: new Dictionary<string, object>
                                     {
                                         { "x-dead-letter-exchange", "orders.dlx" }
                                     });

                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;

                for (int i = 0; i < request.Count; i++)
                {
                    var orderRequest = GenerateRandomOrderRequest(request.ProductsPerOrder);
                    var json = JsonSerializer.Serialize(orderRequest);
                    var body = Encoding.UTF8.GetBytes(json);

                    channel.BasicPublish(exchange: "",
                                         routingKey: queueName,
                                         basicProperties: properties,
                                         body: body);

                    if (i % 1000 == 0) // Loga a cada 1000 pedidos para acompanhar o progresso
                    {
                        _logger.LogInformation("Gerados {Index} de {Count} pedidos de teste...", i + 1, request.Count);
                    }

                    if (request.DelayMs > 0)
                    {
                        await Task.Delay(request.DelayMs); // Atraso opcional entre publicações
                    }
                }
                _logger.LogInformation("Geração de {Count} pedidos de teste finalizada.", request.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro durante a geração de pedidos de teste.");
            }
        });

        // Retorna imediatamente para o cliente
        return Accepted(new { message = $"Geração de {request.Count} pedidos de teste iniciada em segundo plano." });
    }

    // Método auxiliar para gerar um OrderRequest aleatório
    private OrderRequest GenerateRandomOrderRequest(int productsPerOrder)
    {
        var products = new List<ProductRequest>();
        var numberOfProducts = _random.Next(1, Math.Max(2, productsPerOrder * 2 - 1)); // Varia de 1 até quase o dobro do pedido

        for (int i = 0; i < numberOfProducts; i++)
        {
            products.Add(new ProductRequest
            {
                Name = $"Product_{Guid.NewGuid().ToString().Substring(0, 8)}",
                Quantity = _random.Next(1, 5),
                UnitPrice = (decimal)(_random.NextDouble() * 100) + 0.99m // Preço entre 0.99 e 100.98
            });
        }

        return new OrderRequest
        {
            ExternalId = $"TEST_ORDER_{Guid.NewGuid()}",
            Products = products
        };
    }

}

using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.DTOs;
using OrderService.Application.Commands;
using OrderService.Application.Queries;
using OrderService.Domain.Models;
using OrderService.Infrastructure.Queries;
using RabbitMQ.Client;

namespace OrderService.WebApi.Controllers;

[ApiController]
[Route("[controller]")]
[ApiExplorerSettings(GroupName = "v1")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<OrdersController> _logger;
    private readonly IConnectionFactory _connectionFactory;
    private readonly Random _random = new();

    public OrdersController(
        IMediator mediator,
        ILogger<OrdersController> logger,
        IConnectionFactory connectionFactory)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] OrderRequest request, CancellationToken cancellationToken)
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
                Items = request.Items
            };
            
            await _mediator.Send(command, cancellationToken);
            return Accepted(new { message = $"Pedido {request.ExternalId} recebido e enfileirado para processamento." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar o pedido {ExternalId}", request?.ExternalId);
            return StatusCode(500, "Ocorreu um erro interno ao tentar processar o pedido.");
        }
    }

    [HttpGet("{externalId}")]
    public async Task<IActionResult> GetOrder(string externalId, CancellationToken cancellationToken)
    {
        var query = new GetOrderByIdQuery { ExternalId = externalId };
        var result = await _mediator.Send(query, cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] ListOrdersQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost("generate-test-orders")] 
    public IActionResult GenerateTestOrders([FromBody] GenerateOrdersRequest request)
    {
        if (request.Count <= 0 || request.Count > 1000000) // Limita a quantidade de pedidos gerados
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
        var items = new List<OrderItemRequest>();
        var numberOfProducts = _random.Next(1, Math.Max(2, productsPerOrder * 2 - 1)); // Varia de 1 até quase o dobro do pedido

        for (int i = 0; i < numberOfProducts; i++)
        {
            items.Add(new OrderItemRequest
            {
                Name = $"Product_{Guid.NewGuid().ToString().Substring(0, 8)}",
                Quantity = _random.Next(1, 5),
                UnitPrice = (decimal)(_random.NextDouble() * 100) + 0.99m // Preço entre 0.99 e 100.98
            });
        }

        return new OrderRequest
        {
            ExternalId = $"TEST_ORDER_{Guid.NewGuid()}",
            Items = items
        };
    }

}

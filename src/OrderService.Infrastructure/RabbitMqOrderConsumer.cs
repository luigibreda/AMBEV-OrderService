using RabbitMQ.Client;          
using RabbitMQ.Client.Events;   
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.Data;
using OrderService.Application.DTOs;
using OrderService.Domain.Models;
using OrderService.Domain.Enums;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

// Usando o namespace correto para seus serviços
namespace OrderService.Infrastructure;

public class RabbitMqOrderConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RabbitMqOrderConsumer> _logger;
    private readonly ConnectionFactory _connectionFactory;
    private IConnection _connection;
    private IModel _channel;
    private int _processedOrdersCount = 0;
    private Stopwatch _processingStopwatch = new Stopwatch();
    private const int LogIntervalOrders = 1000;
    
    public RabbitMqOrderConsumer(IServiceScopeFactory scopeFactory, ILogger<RabbitMqOrderConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        // A ConnectionFactory é criada aqui, mas a conexão em si será estabelecida no ExecuteAsync.
        // Isso evita que a aplicação falhe ao iniciar se o RabbitMQ estiver offline.
        _connectionFactory = new ConnectionFactory
        {
            HostName = "rabbitmq", // ou use IConfiguration para pegar de appsettings.json
            DispatchConsumersAsync = true // Essencial para o consumidor assíncrono
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Serviço de consumidor de pedidos iniciando.");

        // Registra um método para ser chamado quando a aplicação solicitar o encerramento.
        // Isso garante que a conexão será fechada de forma limpa.
        stoppingToken.Register(() => _logger.LogInformation("Serviço de consumidor de pedidos sendo encerrado."));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Tenta conectar e configurar o consumidor.
                ConnectAndConfigureConsumer(stoppingToken);

                _processedOrdersCount = 0;
                _processingStopwatch.Restart(); 

                _logger.LogInformation("Consumidor conectado e aguardando mensagens.");

                // Mantém o serviço 'vivo' enquanto o token de cancelamento não for acionado.
                // Esta é a correção CRÍTICA. O ExecuteAsync não deve terminar.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Exceção esperada quando o serviço está sendo parado. Não precisa fazer nada.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Não foi possível conectar ao RabbitMQ. Tentando novamente em 10 segundos...");
                _processingStopwatch.Stop(); 
                // Espera um tempo antes de tentar reconectar para não sobrecarregar.
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        
        _logger.LogInformation("Serviço de consumidor de pedidos finalizado.");
    }
    
    private void ConnectAndConfigureConsumer(CancellationToken stoppingToken)
    {
        _connection = _connectionFactory.CreateConnection();
        _channel = _connection.CreateModel();

        // Configura o Dead-Letter Exchange (DLX) para mensagens com erro
        const string deadLetterExchange = "orders.dlx";
        _channel.ExchangeDeclare(deadLetterExchange, ExchangeType.Fanout);
        _channel.QueueDeclare("orders.dead-letter", durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind("orders.dead-letter", deadLetterExchange, routingKey: "");

        // Declara a fila principal com o argumento para enviar mensagens falhas para o DLX
        const string queueName = "orders";
        _channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object> { { "x-dead-letter-exchange", "orders.dlx" } });

        // Controla quantas mensagens o consumidor pega por vez. Essencial para balanceamento e evitar sobrecarga.
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceived;

        _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
    }
    
    private async Task OnMessageReceived(object sender, BasicDeliverEventArgs ea)
    {
        var body = ea.Body.ToArray();
        var json = Encoding.UTF8.GetString(body);
        _logger.LogInformation("Pedido recebido: {Payload}", json);

        // Usar um service scope é a forma correta de obter serviços com ciclo de vida 'scoped' (como o DbContext)
        // dentro de um serviço 'singleton' (como o BackgroundService).
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // É uma boa prática usar opções no Deserializer para torná-lo mais flexível.
            var orderRequest = JsonSerializer.Deserialize<OrderRequest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (orderRequest is null || string.IsNullOrWhiteSpace(orderRequest.ExternalId))
            {
                 _logger.LogWarning("Pedido inválido ou sem ExternalId. Rejeitando mensagem.");
                 _channel.BasicNack(ea.DeliveryTag, false, requeue: false); // Envia para a DLQ
                 return;
            }

            // A verificação de duplicidade é crucial.
            var exists = await dbContext.Orders.AnyAsync(o => o.ExternalId == orderRequest.ExternalId);
            if (exists)
            {
                _logger.LogWarning("Pedido duplicado recebido com ExternalId: {ExternalId}. Ignorando.", orderRequest.ExternalId);
                _channel.BasicAck(ea.DeliveryTag, false); // Confirma a mensagem para removê-la da fila.
                return;
            }

            // Mapeamento do DTO para a entidade do domínio
            var order = new Order
            {
                ExternalId = orderRequest.ExternalId,
                Status = OrderStatus.PROCESSING, // Status inicial mais adequado
                CreatedAt = DateTime.UtcNow,
                Products = orderRequest.Products.Select(p => new Product
                {
                    Name = p.Name,
                    Quantity = p.Quantity,
                    UnitPrice = p.UnitPrice
                }).ToList()
            };

            // A lógica de cálculo deve estar na entidade ou em um serviço de domínio.
            order.CalculateTotalValue(); 
            order.Status = OrderStatus.CALCULATED; // Atualiza o status após o sucesso

            dbContext.Orders.Add(order);
            await dbContext.SaveChangesAsync();

            Interlocked.Increment(ref _processedOrdersCount); // Incrementa o contador de forma thread-safe

            // Log de performance intermediário
            if (_processedOrdersCount % LogIntervalOrders == 0)
            {
                var elapsedMs = _processingStopwatch.ElapsedMilliseconds;
                var ordersPerSecond = elapsedMs > 0 ? (_processedOrdersCount / (elapsedMs / 1000.0)) : 0;
                _logger.LogInformation("Processados {ProcessedCount} pedidos em {ElapsedMs:0} ms. Taxa: {Rate:0.##} pedidos/segundo.",
                                    _processedOrdersCount, elapsedMs, ordersPerSecond);
            }

            // Confirma o processamento (ACK - Acknowledge). A mensagem será removida da fila.
            _channel.BasicAck(ea.DeliveryTag, false);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Erro de deserialização no JSON recebido. Payload: {Payload}", json);
            // Rejeita a mensagem e NÃO a coloca de volta na fila (requeue: false), pois ela nunca será processável.
            // Ela irá para a Dead-Letter Queue (DLQ) para análise manual.
            _channel.BasicNack(ea.DeliveryTag, false, requeue: false);
        }
        catch (DbUpdateException dbEx) // Captura especificamente erros de banco de dados
        {
             _logger.LogError(dbEx, "Erro ao salvar o pedido no banco de dados. Payload: {Payload}", json);
             // Rejeita a mensagem mas permite que seja reenfileirada (requeue: true) se o erro for transitório (ex: deadlock).
             // CUIDADO: Isso pode criar um loop se o erro for permanente. Uma estratégia de retentativa com backoff seria melhor.
             // Por segurança, vamos enviar para a DLQ.
             _channel.BasicNack(ea.DeliveryTag, false, requeue: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao processar pedido. Payload: {Payload}", json);
            _channel.BasicNack(ea.DeliveryTag, false, requeue: false); // Envia para a DLQ
        }
    }

    private void LogFinalPerformance()
    {
        if (_processedOrdersCount > 0 && _processingStopwatch.ElapsedMilliseconds > 0)
        {
            var elapsedMs = _processingStopwatch.ElapsedMilliseconds;
            var ordersPerSecond = (_processedOrdersCount / (elapsedMs / 1000.0));
            _logger.LogInformation("--- Performance Final de Processamento do Consumidor ---");
            _logger.LogInformation("Total de Pedidos Processados: {TotalProcessed}", _processedOrdersCount);
            _logger.LogInformation("Tempo Total de Processamento: {ElapsedSeconds:0.##} segundos ({ElapsedMs:0} ms)", elapsedMs / 1000.0, elapsedMs);
            _logger.LogInformation("Taxa Média de Processamento: {AverageRate:0.##} pedidos/segundo", ordersPerSecond);
            _logger.LogInformation("-------------------------------------------------------");
        }
        else if (_processedOrdersCount == 0 && _processingStopwatch.ElapsedMilliseconds > 0)
        {
            _logger.LogInformation("Nenhum pedido processado durante este ciclo de vida do consumidor (Tempo decorrido: {ElapsedMs:0} ms).", _processingStopwatch.ElapsedMilliseconds);
        }
    }

    public override void Dispose()
    {
        _logger.LogInformation("Dispondo recursos do consumidor.");

        _processingStopwatch.Stop(); 
        LogFinalPerformance();

        _channel?.Close();
        _channel?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
        base.Dispose();
    }
}
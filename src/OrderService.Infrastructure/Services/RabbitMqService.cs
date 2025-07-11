using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderService.Application.Interfaces;
using OrderService.Infrastructure.Settings;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OrderService.Infrastructure.Services;

public class RabbitMqService : IMessageBusService, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqService> _logger;
    private readonly RabbitMqSettings _settings;
    private bool _disposed;

    public RabbitMqService(IOptions<RabbitMqSettings> settings, ILogger<RabbitMqService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var factory = new ConnectionFactory()
        {
            HostName = _settings.HostName,
            Port = _settings.Port,
            UserName = _settings.Username,
            Password = _settings.Password,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    public async Task PublishAsync<T>(T message, string queueName) where T : class
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        try
        {
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;

            _channel.BasicPublish(
                exchange: string.Empty,
                routingKey: queueName,
                basicProperties: properties,
                body: body);

            _logger.LogInformation("Mensagem publicada na fila {QueueName}: {Message}", queueName, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao publicar mensagem na fila {QueueName}", queueName);
            throw;
        }
    }

    public async Task SubscribeAsync<T>(string queueName, Func<T, Task> onMessage) where T : class
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);
        
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(body));
                
                if (message != null)
                {
                    await onMessage(message);
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                else
                {
                    _logger.LogWarning("Mensagem nula recebida da fila {QueueName}", queueName);
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar mensagem da fila {QueueName}", queueName);
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        
        _logger.LogInformation("Consumidor de mensagens iniciado na fila {QueueName}", queueName);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _channel?.Close();
        _channel?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

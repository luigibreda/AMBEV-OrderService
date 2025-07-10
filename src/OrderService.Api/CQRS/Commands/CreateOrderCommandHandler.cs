using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text.Json;

namespace OrderService.CQRS.Commands
{
    public class CreateOrderCommandHandler
    {
        private readonly ConnectionFactory _connectionFactory;
        private readonly ILogger<CreateOrderCommandHandler> _logger;

        public CreateOrderCommandHandler(ConnectionFactory connectionFactory, ILogger<CreateOrderCommandHandler> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public void Handle(CreateOrderCommand command)
        {
            // Exemplo de publicação na fila RabbitMQ
            using var connection = _connectionFactory.CreateConnection();
            using var channel = connection.CreateModel();
            channel.QueueDeclare(queue: "orders", durable: false, exclusive: false, autoDelete: false, arguments: null);
            var message = JsonSerializer.Serialize(command);
            var body = System.Text.Encoding.UTF8.GetBytes(message);
            channel.BasicPublish(exchange: "", routingKey: "orders", basicProperties: null, body: body);
            _logger.LogInformation($"Order {command.ExternalId} published to queue.");
        }
    }
}

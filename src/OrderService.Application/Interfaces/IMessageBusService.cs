using System.Threading.Tasks;

namespace OrderService.Application.Interfaces;

public interface IMessageBusService
{
    Task PublishAsync<T>(T message, string queueName) where T : class;
    Task SubscribeAsync<T>(string queueName, Func<T, Task> onMessage) where T : class;
}

namespace OrderService.Infrastructure.Settings;

public class RabbitMqSettings
{
    public const string SectionName = "RabbitMQ";
    
    public string HostName { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string QueueName { get; set; } = string.Empty;
}

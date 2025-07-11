using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Application.Commands;
using OrderService.Application.Interfaces;
using OrderService.Application.Queries;
using OrderService.Application.Services;
using OrderService.Domain.Interfaces;
using OrderService.Infrastructure;
using OrderService.Infrastructure.Data;
using OrderService.Infrastructure.Queries;
using OrderService.Infrastructure.Repositories;
using OrderService.Infrastructure.Services;
using OrderService.Infrastructure.Settings;
using OrderService.WebApi.Controllers;
using RabbitMQ.Client;

// Declarações de nível superior (top-level statements) devem vir primeiro
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add MediatR
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Conexão com PostgreSQL
builder.Services.AddDbContext<OrderService.Infrastructure.Data.AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
    b => b.MigrationsAssembly("OrderService.Infrastructure")));

// Register repositories
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Register application services
builder.Services.AddScoped<OrderProcessorService>();

// Configure RabbitMQ
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection(RabbitMqSettings.SectionName));
builder.Services.AddSingleton<IMessageBusService, RabbitMqService>();

// Configuração do RabbitMQ
var rabbitMqConfig = builder.Configuration.GetSection("RabbitMQ");

// Registra a ConnectionFactory do RabbitMQ como IConnectionFactory
builder.Services.AddSingleton<IConnectionFactory>(sp => new ConnectionFactory
{
    HostName = rabbitMqConfig["HostName"],
    Port = rabbitMqConfig.GetValue<int>("Port"),
    UserName = rabbitMqConfig["Username"],
    Password = rabbitMqConfig["Password"],
    DispatchConsumersAsync = true,
    AutomaticRecoveryEnabled = true,
    NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
});

// Registra uma instância de ConnectionFactory para ser usada diretamente (se necessário)
builder.Services.AddSingleton<ConnectionFactory>(sp => 
    sp.GetRequiredService<IConnectionFactory>() as ConnectionFactory ?? 
    throw new InvalidOperationException("Falha ao obter ConnectionFactory"));

// Serviços da API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer(); // Swagger
builder.Services.AddSwaggerGen();          // Swagger

// CQRS Handlers
builder.Services.AddScoped<IGetOrderByIdQueryHandler, GetOrderByIdQueryHandler>();

// Register command and query handlers
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, Unit>, CreateOrderCommandHandler>();

// Register the concrete handler for direct injection in OrdersController
builder.Services.AddScoped<GetOrderByIdQueryHandler>();

// Register ILogger
builder.Services.AddLogging();

// Register RabbitMQ consumer as a hosted service
builder.Services.AddHostedService<RabbitMqOrderConsumer>();

// Register the OrdersController
builder.Services.AddScoped<OrdersController>();

// Add Controllers
builder.Services.AddControllers();

var app = builder.Build();

var rabbitMqSettings = builder.Configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>();

// Aplicar migrações apenas se o argumento --migrate for fornecido
if (args.Contains("--migrate"))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderService.Infrastructure.Data.AppDbContext>();
        
        Console.WriteLine("Aplicando migrações do banco de dados...");
        dbContext.Database.Migrate();
        Console.WriteLine("Migrações aplicadas com sucesso!");
        
        // Encerra o aplicativo após aplicar as migrações
        return;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Erro ao aplicar migrações: {ex.Message}");
        Environment.Exit(1);
    }
}

// Se não for uma execução de migração, continua com a inicialização normal do aplicativo
if (!app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderService.Infrastructure.Data.AppDbContext>();
    
    try
    {
        // Verifica se o banco de dados existe e está acessível
        if (dbContext.Database.CanConnect())
        {
            app.Logger.LogInformation("Conexão com o banco de dados estabelecida com sucesso.");
            
            // Verifica se existem migrações pendentes
            var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();
            if (pendingMigrations.Any())
            {
                app.Logger.LogWarning($"Existem migrações pendentes: {string.Join(", ", pendingMigrations)}");
                app.Logger.LogWarning("Execute 'dotnet run -- --migrate' para aplicar as migrações pendentes.");
            }
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Erro ao verificar migrações pendentes");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Start RabbitMQ consumer in development mode
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var messageBusService = scope.ServiceProvider.GetRequiredService<IMessageBusService>();
    var orderProcessor = scope.ServiceProvider.GetRequiredService<OrderProcessorService>();
    
    // Start listening for messages
    _ = messageBusService.SubscribeAsync<CreateOrderCommand>("orders", orderProcessor.ProcessOrderAsync)
        .ContinueWith(t => 
        {
            if (t.Exception != null)
            {
                app.Logger.LogError(t.Exception, "Error starting order consumer");
            }
            return Task.CompletedTask;
        }, TaskContinuationOptions.OnlyOnFaulted);
}

app.UseAuthorization();
app.MapControllers();
app.Run();

// Adiciona a declaração da classe Program para torná-la acessível aos testes
namespace OrderService.WebApi
{
    public partial class Program { }
}

using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure;
using RabbitMQ.Client;

// Declarações de nível superior (top-level statements) devem vir primeiro
var builder = WebApplication.CreateBuilder(args);

// Conexão com PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddScoped<OrderService.Application.Queries.IGetOrderByIdQueryHandler, OrderService.Infrastructure.Queries.GetOrderByIdQueryHandler>();

// Registrar os handlers concretos usados diretamente pelos controladores
builder.Services.AddScoped<OrderService.Application.Commands.CreateOrderCommandHandler>();

// Registrar o GetOrderByIdQueryHandler diretamente para injeção no OrdersController
builder.Services.AddScoped<OrderService.Infrastructure.Queries.GetOrderByIdQueryHandler>();

// Registrar ILogger
builder.Services.AddLogging();

// RabbitMQ
builder.Services.AddHostedService<OrderService.Infrastructure.RabbitMqOrderConsumer>();

// Registra o OrdersController explicitamente para garantir a resolução de todas as dependências
builder.Services.AddScoped<OrderService.WebApi.Controllers.OrdersController>();

var app = builder.Build();

// Aplicar migrações apenas se o argumento --migrate for fornecido
if (args.Contains("--migrate"))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
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
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    
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
app.UseAuthorization();
app.MapControllers();
app.Run();

// Adiciona a declaração da classe Program para torná-la acessível aos testes
namespace OrderService.WebApi
{
    public partial class Program { }
}

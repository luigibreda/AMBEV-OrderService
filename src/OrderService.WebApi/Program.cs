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

// Add MediatR with all handlers from the Application assembly
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly));

// Add AutoMapper
builder.Services.AddAutoMapper(typeof(Program).Assembly);

// Configure PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? 
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, 
        b => b.MigrationsAssembly("OrderService.Infrastructure")));

// Register repositories
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Register application services
builder.Services.AddScoped<OrderProcessorService>();

// Configure RabbitMQ
var rabbitMqSection = builder.Configuration.GetSection(RabbitMqSettings.SectionName);
builder.Services.Configure<RabbitMqSettings>(rabbitMqSection);

// Register RabbitMQ services
builder.Services.AddSingleton<IMessageBusService, RabbitMqService>();

// Register RabbitMQ ConnectionFactory
builder.Services.AddSingleton<IConnectionFactory>(sp => 
{
    var settings = rabbitMqSection.Get<RabbitMqSettings>() ?? 
        throw new InvalidOperationException("RabbitMQ settings not configured");
    
    return new ConnectionFactory
    {
        HostName = settings.HostName,
        Port = settings.Port,
        UserName = settings.Username,
        Password = settings.Password,
        DispatchConsumersAsync = true,
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
    };
});

// Register API services
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null; // Keep PascalCase
    });

// Configure Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo 
    { 
        Title = "Order Service API", 
        Version = "v1",
        Description = "API para gerenciamento de pedidos"
    });
    
    // Configuração de segurança JWT (opcional, pode ser removida se não for usar autenticação)
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            },
            new List<string>()
        }
    });
    
    // Enable XML comments for Swagger
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Register RabbitMQ consumer as a hosted service
builder.Services.AddHostedService<RabbitMqOrderConsumer>();

// Register logging
builder.Services.AddLogging(configure => 
    configure.AddConsole().AddDebug().SetMinimumLevel(LogLevel.Information));

var app = builder.Build();

// Configure the HTTP request pipeline
var isDevelopment = app.Environment.IsDevelopment();

// Always enable Swagger in development, or when SWAGGER_ENABLED is true
var swaggerEnabled = isDevelopment || 
    string.Equals(Environment.GetEnvironmentVariable("SWAGGER_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c => 
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Service API V1");
        c.RoutePrefix = "swagger";
    });
}

// Configure Kestrel to listen on all network interfaces
var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "80";
var url = $"http://0.0.0.0:{port}";

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();

// Configure the app to listen on the specified URL
app.Urls.Add(url);

app.UseAuthorization();

app.MapControllers();

var rabbitMqSettings = app.Configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>();

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

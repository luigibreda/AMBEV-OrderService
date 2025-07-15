using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Application.Commands;
using OrderService.Application.DTOs;
using OrderService.Application.Interfaces;
using OrderService.Application.Queries;
using OrderService.Application.Services;
using OrderService.Application.Behaviors;
using OrderService.Domain.Interfaces;
using OrderService.Infrastructure;
using OrderService.Infrastructure.Data;
using OrderService.Infrastructure.Queries;
using OrderService.Infrastructure.Repositories;
using FluentValidation;
using OrderService.Infrastructure.Services;
using OrderService.Infrastructure.Settings;
using OrderService.WebApi.Controllers;
using RabbitMQ.Client;

// Configuração inicial da aplicação
var builder = WebApplication.CreateBuilder(args);

// Configuração dos serviços da aplicação
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Registra o MediatR com todos os handlers da camada de Application
builder.Services.AddMediatR(cfg => 
{
    cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

// Adiciona o FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(CreateOrderCommand).Assembly);

// Configuração do AutoMapper
builder.Services.AddAutoMapper(typeof(Program).Assembly);

// Configuração do PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? 
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, 
        b => b.MigrationsAssembly("OrderService.Infrastructure")));

builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<AppDbContext>());

// Registro dos repositórios
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Registro dos serviços da aplicação
builder.Services.AddScoped<OrderProcessorService>();

// Configuração do RabbitMQ
var rabbitMqSection = builder.Configuration.GetSection(RabbitMqSettings.SectionName);
builder.Services.Configure<RabbitMqSettings>(rabbitMqSection);

// Registro dos serviços do RabbitMQ
builder.Services.AddSingleton<IMessageBusService, RabbitMqService>();

// Configuração da fábrica de conexão do RabbitMQ
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

// Configuração dos serviços da API
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
options.JsonSerializerOptions.PropertyNamingPolicy = null; // Mantém o padrão PascalCase
    });

// Configuração do Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo 
    { 
        Title = "Order Service API", 
        Version = "v1",
        Description = "API para gerenciamento de pedidos"
    });
    
    // Configuração de segurança JWT
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
    
    // Habilita comentários XML para o Swagger
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Configuração da política CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Registra o consumidor do RabbitMQ como um serviço em background
builder.Services.AddHostedService<RabbitMqOrderConsumer>();

// Configuração do sistema de logs
builder.Services.AddLogging(configure => 
    configure.AddConsole().AddDebug().SetMinimumLevel(LogLevel.Information));

var app = builder.Build();

// Configuração do pipeline de requisições HTTP
var isDevelopment = app.Environment.IsDevelopment();

// Habilita o Swagger em ambiente de desenvolvimento ou quando a variável SWAGGER_ENABLED estiver definida
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

// Configura o Kestrel para escutar em todas as interfaces de rede
var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "80";
var url = $"http://0.0.0.0:{port}";

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();

// Configura a aplicação para escutar na URL especificada
app.Urls.Add(url);

app.UseAuthorization();

app.MapControllers();

var rabbitMqSettings = app.Configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>();

// Tratamento de migrações quando a flag --migrate for passada
if (args.Contains("--migrate"))
{
    try
    {
        Console.WriteLine("Aplicando migrações do banco de dados...");
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

// Se não for uma execução de migração, continua com a inicialização normal
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

// Inicia o consumidor do RabbitMQ em modo de desenvolvimento
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var messageBusService = scope.ServiceProvider.GetRequiredService<IMessageBusService>();
    var orderProcessor = scope.ServiceProvider.GetRequiredService<OrderProcessorService>();
    
    // Inicia a escuta de mensagens
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

// Torna a classe Program acessível para os testes
namespace OrderService.WebApi
{
    public partial class Program { }
}

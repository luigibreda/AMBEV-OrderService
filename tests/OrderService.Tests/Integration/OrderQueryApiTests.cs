using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Application.Commands;
using OrderService.Application.Queries;
using OrderService.Domain.Enums;
using OrderService.Domain.Models;
using OrderService.Infrastructure;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Xunit;
using FluentAssertions;

namespace OrderService.Tests.Integration;

public class OrderQueryApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<OrderService.WebApi.Program> _factory;
    private HttpClient _httpClient;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        _factory = new WebApplicationFactory<OrderService.WebApi.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string>("DisableMigrations", "true")
                    });
                });
                
                builder.ConfigureServices(services =>
                {
                    // Remove o contexto padrão e injeta o de teste
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>((sp, options) =>
                    {
                        options.UseNpgsql(
                            _dbContainer.GetConnectionString() + ";Pooling=false",
                            npgsqlOptions => npgsqlOptions.MigrationsAssembly("OrderService.Infrastructure")
                        );
                    });

                    // Registrar os handlers necessários
                    services.AddScoped<OrderService.Application.Queries.IGetOrderByIdQueryHandler, OrderService.Infrastructure.Queries.GetOrderByIdQueryHandler>();
                    services.AddLogging();
                    
                    // Configurar ConnectionFactory para o CreateOrderCommandHandler
                    services.AddSingleton<IConnectionFactory>(sp => new ConnectionFactory()
                    {
                        HostName = "localhost",
                        DispatchConsumersAsync = true
                    });
                    services.AddScoped<OrderService.Application.Commands.CreateOrderCommandHandler>();

                    // Remove o consumidor RabbitMQ para não interferir nos testes
                    var consumerDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == "RabbitMqOrderConsumer");
                    if (consumerDescriptor != null)
                        services.Remove(consumerDescriptor);
                });
            });

        // Aplica as migrações
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Garante que o banco de dados existe
            await dbContext.Database.EnsureCreatedAsync();
            
            // Aplica as migrações
            await dbContext.Database.MigrateAsync();
            
            // Verifica se as tabelas foram criadas
            var connection = dbContext.Database.GetDbConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'Orders');";
            var tableExists = await command.ExecuteScalarAsync();
            
            if (tableExists is not bool tableExistsBool || !tableExistsBool)
            {
                throw new Exception("A tabela 'Orders' não foi criada corretamente.");
            }
        }

        _httpClient = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.StopAsync();
    }

    [Fact]
    public async Task GetOrder_Should_ReturnNotFound_When_OrderDoesNotExist()
    {
        var response = await _httpClient.GetAsync("/orders/NON_EXISTENT_ID");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrder_Should_ReturnOrder_When_OrderExists()
    {
        var orderId = "TEST-123";
        var order = new Order
        {
            ExternalId = orderId,
            Status = OrderStatus.CALCULATED,
            CreatedAt = DateTime.UtcNow,
            TotalValue = 100
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        var response = await _httpClient.GetAsync($"/orders/{orderId}");
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Erro na requisição: {response.StatusCode}. Detalhes: {errorContent}");
        }
        
        var responseString = await response.Content.ReadAsStringAsync();
        responseString.Should().Contain(orderId);
        responseString.Should().Contain("100");
    }
}
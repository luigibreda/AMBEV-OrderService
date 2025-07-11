using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OrderService.Application.Commands;
using OrderService.Application.DTOs;
using OrderService.Application.Interfaces;
using OrderService.Application.Queries;
using OrderService.Domain.Enums;
using OrderService.Domain.Models;
using OrderService.Infrastructure.Data;
using OrderService.Infrastructure.Queries;
using OrderService.Infrastructure.Services;
using OrderService.Infrastructure.Settings;
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
                
                    // Configuração dos serviços de teste
                    builder.ConfigureServices(services =>
                    {
                        // Remove o DbContext padrão e injeta o de teste
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

                        // Registra o MediatR e seus handlers
                        services.AddMediatR(cfg => 
                            cfg.RegisterServicesFromAssembly(typeof(GetOrderByIdQuery).Assembly));
                        
                        services.AddScoped<IRequestHandler<GetOrderByIdQuery, OrderResponse?>, GetOrderByIdQueryHandler>();
                        services.AddLogging();
                        
                        // Mock do serviço de mensageria
                        var mockMessageBusService = new Mock<IMessageBusService>();
                        mockMessageBusService
                            .Setup(m => m.PublishAsync(It.IsAny<object>(), It.IsAny<string>()))
                            .Returns(Task.CompletedTask);
                            
                        services.AddSingleton(mockMessageBusService.Object);

                        // Configurações do RabbitMQ
                        var rabbitMqSettings = new RabbitMqSettings
                        {
                            HostName = "localhost",
                            Port = 5672,
                            Username = "guest",
                            Password = "guest",
                            QueueName = "orders"
                        };
                        
                        services.AddSingleton(Options.Create(rabbitMqSettings));

                        // Registra os handlers de comandos
                        services.AddScoped<OrderService.Application.Commands.CreateOrderCommandHandler>();

                        // Remove o consumidor do RabbitMQ para evitar interferência nos testes
                        var consumerDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == "RabbitMqOrderConsumer");
                        if (consumerDescriptor != null)
                            services.Remove(consumerDescriptor);
                });
            });

        // Aplica as migrações e garante que o banco de dados está limpo
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            try
            {
                // Garante que o banco de dados foi criado e migrado
                await dbContext.Database.EnsureCreatedAsync();
                
                // Verifica se a tabela de pedidos existe
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
            catch (Exception ex)
            {
                throw new Exception("Erro ao configurar o banco de dados de teste", ex);
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
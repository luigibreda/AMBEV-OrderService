using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json; 
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderService.Infrastructure.Data;
using OrderService.Application.DTOs;
using OrderService.Domain.Enums;
using OrderService.Domain.Models;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;
using FluentAssertions;
using RabbitMQ.Client;

namespace OrderService.Tests.E2E;

public class FullOrderFlowTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder().WithImage("rabbitmq:3-management-alpine").Build();

    private WebApplicationFactory<OrderService.WebApi.Program> _factory;
    private HttpClient _httpClient;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        _factory = new WebApplicationFactory<OrderService.WebApi.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbContextDescriptor != null) services.Remove(dbContextDescriptor);
                    services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_dbContainer.GetConnectionString()));

                    var rabbitFactoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ConnectionFactory));
                    if (rabbitFactoryDescriptor != null) services.Remove(rabbitFactoryDescriptor);
                    services.AddSingleton(sp => new ConnectionFactory
                    {
                        HostName = _rabbitMqContainer.Hostname, 
                        Port = _rabbitMqContainer.GetMappedPublicPort(5672), 
                        DispatchConsumersAsync = true
                    });
                });
            });

        _httpClient = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.StopAsync();
        await _rabbitMqContainer.StopAsync();
    }

    // [Fact]
    // public async Task CreateOrder_WhenPosted_ShouldBeProcessedAndRetrievable()
    // {
    //     // Arrange (Preparar)
    //     var externalId = $"E2E-ORDER-{Guid.NewGuid()}";
    //     var orderRequest = new OrderRequest
    //     {
    //         ExternalId = externalId,
    //         Products = new List<ProductRequest>
    //         {
    //             new ProductRequest { Name = "E2E Test Product", Quantity = 2, UnitPrice = 50.25m } // Total: 100.50
    //         }
    //     };

    //     // Act - Parte 1: Enviar o pedido para a API de ingestão
    //     var postResponse = await _httpClient.PostAsJsonAsync("/orders", orderRequest);

    //     // Assert - Parte 1: Verificar se a API aceitou o pedido
    //     postResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
        
    //     // Act & Assert - Parte 2: Tentar buscar o pedido e verificar se foi processado
    //     // Como o sistema é assíncrono, precisamos esperar e tentar algumas vezes.
    //     OrderResponse retrievedOrder = null;
    //     for (int i = 0; i < 15; i++) // Tenta por até 15 segundos
    //     {
    //         await Task.Delay(1000); // Espera 1 segundo entre as tentativas
    //         var getResponse = await _httpClient.GetAsync($"/orders/{externalId}");
            
    //         if (getResponse.IsSuccessStatusCode)
    //         {
    //             retrievedOrder = await getResponse.Content.ReadFromJsonAsync<OrderResponse>();
    //             break; // Encontrou o pedido, pode sair do loop
    //         }
    //     }

    //     // Assert Final: Verificar se o pedido retornado está correto
    //     retrievedOrder.Should().NotBeNull("o pedido deveria ter sido processado e encontrado na API de consulta.");
    //     retrievedOrder.ExternalId.Should().Be(externalId);
    //     retrievedOrder.TotalValue.Should().Be(100.50m);
    //     retrievedOrder.Status.Should().Be(OrderStatus.CALCULATED.ToString());
    // }
}
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Data; 
using OrderService.Models;
using OrderService.Enums;
using Testcontainers.PostgreSql;
using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderService.Tests.Integration;

public class OrderQueryApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<Program> _factory;
    private HttpClient _httpClient;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_dbContainer.GetConnectionString()));
                    
                    var consumerDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == "RabbitMqOrderConsumer");
                    if(consumerDescriptor != null)
                    {
                        services.Remove(consumerDescriptor);
                    }
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

        response.EnsureSuccessStatusCode();
        var responseString = await response.Content.ReadAsStringAsync();
        responseString.Should().Contain(orderId);
        responseString.Should().Contain("100");
    }
}
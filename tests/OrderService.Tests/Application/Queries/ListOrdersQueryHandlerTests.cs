using Bogus;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OrderService.Application.DTOs;
using OrderService.Application.Queries;
using OrderService.Domain.Models;
using OrderService.Infrastructure.Data;
using Xunit;

namespace OrderService.Tests.Application.Queries;

public class ListOrdersQueryHandlerTests
{
    private readonly AppDbContext _context;
    
    private readonly Faker<OrderItem> _orderItemFaker;
    private readonly Faker<Order> _orderFaker;

    public ListOrdersQueryHandlerTests()
    {
        // Configura o DbContext para usar um banco de dados em memória
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // DB único por teste
            .Options;

        _context = new AppDbContext(options);

        // Mock do Logger usando NSubstitute
        

        // Configura o Bogus Faker para OrderItem
        _orderItemFaker = new Faker<OrderItem>()
            .RuleFor(oi => oi.Name, f => f.Commerce.ProductName())
            .RuleFor(oi => oi.Quantity, f => f.Random.Number(1, 10))
            .RuleFor(oi => oi.UnitPrice, f => f.Finance.Amount(5, 100));

        // Configura o Bogus Faker para Order
        _orderFaker = new Faker<Order>()
            .RuleFor(o => o.ExternalId, f => f.Random.Guid().ToString())
            .RuleFor(o => o.Status, f => f.PickRandom<OrderService.Domain.Enums.OrderStatus>())
            .RuleFor(o => o.CreatedAt, f => f.Date.Past(1))
            .RuleFor(o => o.Items, f => _orderItemFaker.Generate(f.Random.Number(1, 5)))
            .FinishWith((f, o) =>
            {
                o.CalculateTotalValue(); // Garante que o valor total seja calculado
            });
    }

    [Fact]
    public async Task Handle_ShouldReturnCorrectlyPagedOrders_WhenOrdersExist()
    {
        // Arrange
        // Gera 25 pedidos falsos usando Bogus
        var orders = _orderFaker.Generate(25);
        await _context.Orders.AddRangeAsync(orders);
        await _context.SaveChangesAsync();

        var handler = new ListOrdersQueryHandler(_context);
        var query = new ListOrdersQuery { Page = 2, PageSize = 10 };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
                result.TotalItems.Should().Be(25);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
                result.Data.Should().HaveCount(10);

        // Verifica se os itens da página 2 estão corretos
        var expectedItems = orders.OrderByDescending(o => o.CreatedAt).Skip(10).Take(10).ToList();
                        result.Data.Select(i => i.ExternalId).Should().BeEquivalentTo(expectedItems.Select(e => e.ExternalId));
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyList_WhenNoOrdersExist()
    {
        // Arrange
        var handler = new ListOrdersQueryHandler(_context);
        var query = new ListOrdersQuery { Page = 1, PageSize = 10 };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
                result.TotalItems.Should().Be(0);
                result.Data.Should().BeEmpty();
    }
}

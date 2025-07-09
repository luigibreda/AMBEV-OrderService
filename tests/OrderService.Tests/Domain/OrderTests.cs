using OrderService.Models;
using Xunit;
using FluentAssertions;

public class OrderTests
{
    [Fact]
    public void CalculateTotalValue_Should_SumProductTotals_Correctly()
    {
        var order = new Order
        {
            Products = new List<Product>
            {
                new Product { Quantity = 2, UnitPrice = 10.50m }, // Total = 21.00
                new Product { Quantity = 1, UnitPrice = 5.00m },  // Total = 5.00
                new Product { Quantity = 3, UnitPrice = 1.50m }   // Total = 4.50
            }
        };

        order.CalculateTotalValue();

        decimal expectedTotal = 30.50m;
        order.TotalValue.Should().Be(expectedTotal);
    }

    [Fact]
    public void CalculateTotalValue_Should_BeZero_When_ProductsListIsEmpty()
    {
        var order = new Order
        {
            Products = new List<Product>()
        };

        order.CalculateTotalValue();

        order.TotalValue.Should().Be(0);
    }
}
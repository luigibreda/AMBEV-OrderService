using OrderService.Application.Commands;
using OrderService.Domain.Enums;
using OrderService.Domain.Models;

namespace OrderService.Application.Mappers;

public static class OrderMapper
{
    public static Order ToOrder(this CreateOrderCommand command)
    {
        var order = new Order
        {
            ExternalId = command.ExternalId,
            Status = OrderStatus.RECEIVED,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var item in command.Items)
        {
            order.Items.Add(new OrderItem
            {
                Name = item.Name,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            });
        }

        order.CalculateTotalValue();

        return order;
    }
}

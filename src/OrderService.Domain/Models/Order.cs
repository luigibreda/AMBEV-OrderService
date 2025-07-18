using OrderService.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OrderService.Domain.Models;

public class Order
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public decimal TotalValue { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.RECEIVED;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; set; } = new();

    public void CalculateTotalValue()
    {
        // Garante que a lista de produtos não seja nula antes de tentar somar
        if (Items != null && Items.Any())
        {
            TotalValue = Items.Sum(p => p.Total);
        }
        else
        {
            TotalValue = 0;
        }
    }
}

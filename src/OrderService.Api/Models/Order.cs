using AMBEV_OrderService.Enums;

namespace OrderService.Models;

public class Order
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public decimal TotalValue { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.RECEIVED;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Product> Products { get; set; } = new();
    
    public void CalculateTotalValue()
    {
        // Garante que a lista de produtos não seja nula antes de tentar somar
        if (Products != null && Products.Any())
        {
            TotalValue = Products.Sum(p => p.Total);
        }
        else
        {
            TotalValue = 0;
        }
    }
}

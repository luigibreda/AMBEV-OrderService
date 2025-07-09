namespace OrderService.Models;

public class Order
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public decimal TotalValue { get; set; }
    public string Status { get; set; } = "RECEIVED";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Product> Products { get; set; } = new();
}

namespace OrderService.Application.DTOs;

public class ProductResponse
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}

public class OrderResponse
{
    public string ExternalId { get; set; } = string.Empty;
    public decimal TotalValue { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<ProductResponse> Products { get; set; } = new();
}

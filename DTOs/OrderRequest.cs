namespace OrderService.DTOs;

public class ProductRequest
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class OrderRequest
{
    public string ExternalId { get; set; } = string.Empty;
    public List<ProductRequest> Products { get; set; } = new();
}

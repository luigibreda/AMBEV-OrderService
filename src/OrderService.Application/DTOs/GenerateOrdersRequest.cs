namespace OrderService.Application.DTOs;

public class GenerateOrdersRequest
{
    public int Count { get; set; }
    public int ProductsPerOrder { get; set; } = 1;
    public int DelayMs { get; set; } = 0;
}

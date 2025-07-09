namespace OrderService.DTOs;

public class OrderQueryParams
{
    public string? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

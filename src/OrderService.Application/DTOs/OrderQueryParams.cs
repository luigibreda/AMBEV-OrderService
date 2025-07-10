namespace OrderService.Application.DTOs;

public class OrderQueryParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    // public string? Status { get; set; } // Descomente se quiser filtrar por status
}

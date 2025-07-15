using System.Collections.Generic;

namespace OrderService.Application.DTOs;

public class PagedResult<T>
{
    public int TotalItems { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IEnumerable<T> Data { get; set; } = new List<T>();
}

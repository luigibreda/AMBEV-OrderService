using MediatR;
using OrderService.Application.DTOs;
using OrderService.Domain.Models;

namespace OrderService.Application.Queries;

public class ListOrdersQuery : IRequest<PagedResult<OrderResponse>>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

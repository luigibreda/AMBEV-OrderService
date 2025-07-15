using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.DTOs;
using OrderService.Application.Interfaces;
using OrderService.Domain.Models;

namespace OrderService.Application.Queries;

public class ListOrdersQueryHandler : IRequestHandler<ListOrdersQuery, PagedResult<OrderResponse>>
{
    private readonly IApplicationDbContext _context;

    public ListOrdersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<OrderResponse>> Handle(ListOrdersQuery request, CancellationToken cancellationToken)
    {
        var queryable = _context.Orders
            .Include(o => o.Items)
            .AsQueryable();

        if (request.StartDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(request.StartDate.Value, DateTimeKind.Utc);
            queryable = queryable.Where(o => o.CreatedAt >= startUtc);
        }

        if (request.EndDate.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(
                request.EndDate.Value.Date.AddDays(1).AddTicks(-1), // Ajusta para o último instante do dia
                DateTimeKind.Utc
            );
            queryable = queryable.Where(o => o.CreatedAt <= endUtc);
        }

        var totalItems = await queryable.CountAsync(cancellationToken);

        var orders = await queryable
            .OrderByDescending(o => o.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var response = orders.Select(order => new OrderResponse
        {
            ExternalId = order.ExternalId,
            TotalValue = order.TotalValue,
            Status = order.Status.ToString(),
            CreatedAt = order.CreatedAt,
            Items = order.Items.Select(p => new OrderItemResponse
            {
                Name = p.Name,
                Quantity = p.Quantity,
                UnitPrice = p.UnitPrice,
                Total = p.Total
            }).ToList()
        });

        return new PagedResult<OrderResponse>
        {
            TotalItems = totalItems,
            Page = request.Page,
            PageSize = request.PageSize,
            Data = response
        };
    }
}

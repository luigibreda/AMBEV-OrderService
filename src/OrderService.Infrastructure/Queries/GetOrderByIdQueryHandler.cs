
using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.DTOs;
using OrderService.Application.Queries;
using OrderService.Infrastructure.Data;

namespace OrderService.Infrastructure.Queries
{
    public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderResponse?>
    {
        private readonly AppDbContext _context;

        public GetOrderByIdQueryHandler(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<OrderResponse?> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            
            var order = await _context.Orders
                .Include(o => o.Products)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.ExternalId == query.ExternalId, cancellationToken);

            if (order == null) 
                return null;

            return new OrderResponse
            {
                ExternalId = order.ExternalId,
                TotalValue = order.TotalValue,
                Status = order.Status.ToString(),
                CreatedAt = order.CreatedAt,
                Products = order.Products.Select(p => new ProductResponse
                {
                    Name = p.Name,
                    Quantity = p.Quantity,
                    UnitPrice = p.UnitPrice,
                    Total = p.Total
                }).ToList()
            };
        }
    }
}


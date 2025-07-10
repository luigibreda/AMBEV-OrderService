
using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure;
using OrderService.Application.DTOs;
using OrderService.Application.Queries;

namespace OrderService.Infrastructure.Queries
{
    public class GetOrderByIdQueryHandler : IGetOrderByIdQueryHandler
    {
        private readonly AppDbContext _context;

        public GetOrderByIdQueryHandler(AppDbContext context)
        {
            _context = context;
        }

        public async Task<OrderResponse?> Handle(GetOrderByIdQuery query)
        {
            var order = await _context.Orders
                .Include(o => o.Products)
                .FirstOrDefaultAsync(o => o.ExternalId == query.ExternalId);

            if (order == null) return null;

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


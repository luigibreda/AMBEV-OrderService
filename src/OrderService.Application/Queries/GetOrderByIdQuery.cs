using MediatR;
using OrderService.Application.DTOs;

namespace OrderService.Application.Queries
{
    public class GetOrderByIdQuery : IRequest<OrderResponse?>
    {
        public required string ExternalId { get; set; }
    }
}

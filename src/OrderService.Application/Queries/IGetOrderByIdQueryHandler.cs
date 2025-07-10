namespace OrderService.Application.Queries
{
    using System.Threading.Tasks;

    public interface IGetOrderByIdQueryHandler
    {
        Task<OrderService.Application.DTOs.OrderResponse?> Handle(GetOrderByIdQuery query);
    }
}

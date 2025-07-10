using System.Collections.Generic;
using OrderService.DTOs;

namespace OrderService.CQRS.Commands
{
    public class CreateOrderCommand
    {
        public string ExternalId { get; set; }
        public List<ProductRequest> Products { get; set; }
    }
}

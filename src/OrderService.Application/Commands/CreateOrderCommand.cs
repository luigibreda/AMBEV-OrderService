using System.Collections.Generic;
using OrderService.Application.DTOs;

namespace OrderService.Application.Commands
{
    public class CreateOrderCommand
    {
        public string ExternalId { get; set; }
        public List<ProductRequest> Products { get; set; }
    }
}

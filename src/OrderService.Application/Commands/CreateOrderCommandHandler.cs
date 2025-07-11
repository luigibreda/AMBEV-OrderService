using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderService.Application.DTOs;
using OrderService.Application.Interfaces;

namespace OrderService.Application.Commands
{
    public class CreateOrderCommand : IRequest<Unit>
    {
        public string ExternalId { get; set; } = string.Empty;
        public List<ProductRequest> Products { get; set; } = new();
    }

    public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Unit>
    {
        private readonly IMessageBusService _messageBusService;
        private readonly ILogger<CreateOrderCommandHandler> _logger;

        public CreateOrderCommandHandler(
            IMessageBusService messageBusService,
            ILogger<CreateOrderCommandHandler> logger)
        {
            _messageBusService = messageBusService;
            _logger = logger;
        }

        public async Task<Unit> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Pedido: {ExternalId} publicado na fila.", request.ExternalId);
            
            await _messageBusService.PublishAsync(request, "orders");
            
            return Unit.Value;
        }
    }
}

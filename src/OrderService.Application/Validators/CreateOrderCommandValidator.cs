using FluentValidation;
using OrderService.Application.Commands;
using OrderService.Domain.Interfaces;

namespace OrderService.Application.Validators;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    private readonly IOrderRepository _orderRepository;

    public CreateOrderCommandValidator(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;

        RuleFor(command => command.ExternalId)
            .NotEmpty().WithMessage("O ExternalId do pedido não pode ser vazio.")
            .MustAsync(async (externalId, cancellationToken) => 
            {
                var existingOrder = await _orderRepository.GetByExternalIdAsync(externalId);
                return existingOrder == null;
            })
            .WithMessage(command => $"O pedido com o ExternalId '{command.ExternalId}' já existe.");

        RuleFor(command => command.Items)
            .NotEmpty().WithMessage("O pedido deve conter pelo menos um item.");
    }
}

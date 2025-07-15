using System;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderService.Application.Commands;
using OrderService.Application.Mappers;
using OrderService.Domain.Interfaces;

namespace OrderService.Application.Services;

public class OrderProcessorService
{
    private readonly ILogger<OrderProcessorService> _logger;
    private readonly IOrderRepository _orderRepository;

    public OrderProcessorService(
        ILogger<OrderProcessorService> logger,
        IOrderRepository orderRepository)
    {
        _logger = logger;
        _orderRepository = orderRepository;
    }

    public async Task ProcessOrderAsync(CreateOrderCommand command)
    {
        _logger.LogInformation("Processando pedido {ExternalId}", command.ExternalId);

        try
        {
            var order = command.ToOrder();

            await _orderRepository.AddAsync(order);
            var result = await _orderRepository.UnitOfWork.CommitAsync();

            if (result)
            {
                _logger.LogInformation("Pedido {ExternalId} processado com sucesso", command.ExternalId);
            }
            else
            {
                _logger.LogError("Falha ao salvar o pedido {ExternalId}", command.ExternalId);
            }
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Falha na validação ao processar o pedido {ExternalId}: {Errors}", command.ExternalId, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar o pedido {ExternalId}", command.ExternalId);
            throw;
        }
    }
}

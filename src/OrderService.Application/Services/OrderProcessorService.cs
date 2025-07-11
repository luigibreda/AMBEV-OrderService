using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OrderService.Application.Commands;
using OrderService.Application.DTOs;
using OrderService.Domain.Enums;
using OrderService.Domain.Interfaces;
using OrderService.Domain.Models;

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
            // Verifica se o pedido já existe
            var existingOrder = await _orderRepository.GetByExternalIdAsync(command.ExternalId);
            if (existingOrder != null)
            {
                _logger.LogWarning("Pedido {ExternalId} já existe", command.ExternalId);
                return;
            }
            
            var order = new Order
            {
                ExternalId = command.ExternalId,
                Status = OrderStatus.RECEIVED,
                CreatedAt = DateTime.UtcNow
            };
            
            foreach (var product in command.Products)
            {
                order.Products.Add(new Product
                {
                    Name = product.Name,
                    Quantity = product.Quantity,
                    UnitPrice = product.UnitPrice
                });
            }
            
            order.CalculateTotalValue();
            
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar o pedido {ExternalId}", command.ExternalId);
            throw;
        }
    }
}

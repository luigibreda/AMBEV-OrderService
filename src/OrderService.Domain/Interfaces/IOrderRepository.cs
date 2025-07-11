using System.Collections.Generic;
using System.Threading.Tasks;
using OrderService.Domain.Models;

namespace OrderService.Domain.Interfaces;

public interface IOrderRepository
{
    Task<Order> GetByIdAsync(int id);
    Task<Order> GetByExternalIdAsync(string externalId);
    Task<IEnumerable<Order>> GetAllAsync();
    Task AddAsync(Order order);
    void Update(Order order);
    void Remove(Order order);
    Task<bool> SaveChangesAsync();
    
    IUnitOfWork UnitOfWork { get; }
}

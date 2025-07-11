using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Interfaces;
using OrderService.Domain.Models;
using OrderService.Infrastructure.Data;

namespace OrderService.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public OrderRepository(AppDbContext context)
    {
        _context = context;
    }

    public IUnitOfWork UnitOfWork => (IUnitOfWork)_context;

    public async Task<Order> GetByIdAsync(int id)
    {
        return await _context.Orders
            .Include(o => o.Products)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Order> GetByExternalIdAsync(string externalId)
    {
        return await _context.Orders
            .Include(o => o.Products)
            .FirstOrDefaultAsync(o => o.ExternalId == externalId);
    }

    public async Task<IEnumerable<Order>> GetAllAsync()
    {
        return await _context.Orders
            .Include(o => o.Products)
            .ToListAsync();
    }

    public async Task AddAsync(Order order)
    {
        await _context.Orders.AddAsync(order);
    }

    public void Update(Order order)
    {
        _context.Entry(order).State = EntityState.Modified;
    }

    public void Remove(Order order)
    {
        _context.Orders.Remove(order);
    }

    public async Task<bool> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync() > 0;
    }
}

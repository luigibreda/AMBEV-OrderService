using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Models;

namespace OrderService.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Order> Orders { get; set; }
    DbSet<OrderItem> OrderItems { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

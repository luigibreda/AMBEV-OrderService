using Microsoft.EntityFrameworkCore;
using OrderService.Models;

namespace OrderService.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>()
            .HasIndex(o => o.ExternalId)
            .IsUnique();

        modelBuilder.Entity<Order>()
            .Property(o => o.Status)
            .HasConversion<string>(); 

        modelBuilder.Entity<Order>()
            .HasMany(o => o.Products)
            .WithOne(p => p.Order)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

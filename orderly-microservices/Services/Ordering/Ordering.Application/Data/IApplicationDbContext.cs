using Microsoft.EntityFrameworkCore;

namespace Ordering.Application.Data;

public interface IApplicationDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<MenuItem> MenuItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

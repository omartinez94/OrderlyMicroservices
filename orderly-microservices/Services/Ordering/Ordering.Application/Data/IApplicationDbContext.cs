using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Ordering.Application.Data;

public interface IApplicationDbContext : IOutboxDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<MenuItem> MenuItems { get; }
    DbSet<OrderBill> OrderBills { get; }
}
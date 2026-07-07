using System.Reflection;
using BuildingBlocks.Messaging.Outbox;
using Ordering.Application.Data;

namespace Ordering.Infrastructure.Data;

public class ApplicationDBContext(DbContextOptions<ApplicationDBContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<OrderBill> OrderBills => Set<OrderBill>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.ApplyConfiguration(new OutboxMessageConfiguration());

        base.OnModelCreating(builder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<NodaTime.Instant>()
            .HaveConversion<InstantConverter>();

        base.ConfigureConventions(configurationBuilder);
    }
}

public class InstantConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<NodaTime.Instant, DateTime>
{
    public InstantConverter()
        : base(
            v => v.ToDateTimeUtc(),
            v => NodaTime.Instant.FromDateTimeUtc(DateTime.SpecifyKind(v, DateTimeKind.Utc)))
    {
    }
}
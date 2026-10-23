using System.Reflection;
using BuildingBlocks.Entities.Interfaces;
using BuildingBlocks.Identifiers;
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
    public DbSet<OutboxDeadMessage> OutboxDeadMessages => Set<OutboxDeadMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.ApplyConfiguration(new OutboxMessageConfiguration());
        builder.ApplyConfiguration(new OutboxDeadMessageConfiguration());

        base.OnModelCreating(builder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<NodaTime.Instant>()
            .HaveConversion<InstantConverter>();

        base.ConfigureConventions(configurationBuilder);
    }

    public override int SaveChanges()
    {
        EnsureShortIds();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureShortIds();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnsureShortIds()
    {
        IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>? entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added);

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry? entry in entries)
        {
            if (entry.Entity is IShortIdentifiable identifiable && string.IsNullOrWhiteSpace(identifiable.ShortId))
            {
                identifiable.ShortId = ShortIdGenerator.Generate(EntityPrefix.Order);
            }
        }
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

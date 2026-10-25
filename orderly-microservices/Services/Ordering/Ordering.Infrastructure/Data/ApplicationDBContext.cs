using System.Reflection;
using BuildingBlocks.Entities.Interfaces;
using BuildingBlocks.Identifiers;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Multitenancy;
using Microsoft.AspNetCore.Http;
using Ordering.Application.Data;

namespace Ordering.Infrastructure.Data;

public class ApplicationDBContext(
    DbContextOptions<ApplicationDBContext> options,
    ICurrentRestaurantProvider? restaurantProvider = null) : DbContext(options), IApplicationDbContext
{
    private readonly ICurrentRestaurantProvider _restaurantProvider =
        restaurantProvider ?? new ClaimsRestaurantProvider(new HttpContextAccessor());

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<OrderBill> OrderBills => Set<OrderBill>();
    public DbSet<RestaurantDailySequence> RestaurantDailySequences => Set<RestaurantDailySequence>();
    public DbSet<RestaurantSetting> RestaurantSettings => Set<RestaurantSetting>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<OutboxDeadMessage> OutboxDeadMessages => Set<OutboxDeadMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.ApplyConfiguration(new OutboxMessageConfiguration());
        builder.ApplyConfiguration(new OutboxDeadMessageConfiguration());

        builder.Entity<Order>().HasQueryFilter(o =>
            o.RestaurantId == _restaurantProvider.RestaurantId);

        base.OnModelCreating(builder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<NodaTime.Instant>()
            .HaveConversion<InstantConverter>();

        configurationBuilder
            .Properties<NodaTime.LocalDate>()
            .HaveConversion<LocalDateConverter>();

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

public class LocalDateConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<NodaTime.LocalDate, DateOnly>
{
    public LocalDateConverter()
        : base(
            d => new DateOnly(d.Year, d.Month, d.Day),
            d => new NodaTime.LocalDate(d.Year, d.Month, d.Day))
    {
    }
}

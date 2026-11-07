using Assistant.API.Domain.Entities;

namespace Assistant.API.Infrastructure.Persistence;

/// <summary>
/// Assistant's relational store in PostgreSQL. Owns tenant AI quota configurations,
/// the append-only <see cref="AiUsageLedger"/> billing audit log, and the transactional outbox tables.
/// </summary>
public sealed class AiDbContext : DbContext, IOutboxDbContext
{
    private readonly ICurrentRestaurantProvider? _restaurantProvider;

    public AiDbContext(DbContextOptions<AiDbContext> options)
        : base(options)
    {
    }

    public AiDbContext(
        DbContextOptions<AiDbContext> options,
        ICurrentRestaurantProvider restaurantProvider)
        : base(options)
    {
        _restaurantProvider = restaurantProvider;
    }

    public DbSet<RestaurantAiQuotaConfig> QuotaConfigs => Set<RestaurantAiQuotaConfig>();
    public DbSet<AiUsageLedger> UsageLedgers => Set<AiUsageLedger>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<OutboxDeadMessage> OutboxDeadMessages => Set<OutboxDeadMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxDeadMessageConfiguration());

        if (_restaurantProvider is not null)
        {
            modelBuilder.ApplyTenantFilter<RestaurantAiQuotaConfig>(() => _restaurantProvider.RestaurantId);
            modelBuilder.ApplyTenantFilter<AiUsageLedger>(() => _restaurantProvider.RestaurantId);
        }

        base.OnModelCreating(modelBuilder);
    }
}

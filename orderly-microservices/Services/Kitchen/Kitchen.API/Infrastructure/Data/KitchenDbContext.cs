using BuildingBlocks.Messaging.Outbox;

namespace Kitchen.API.Infrastructure.Data;

/// <summary>
/// Kitchen's relational store. Owns the ticket + station aggregates plus
/// the <see cref="OutboxMessage"/> table backing the transactional outbox. Configurations live in <c>Configurations/*Configuration.cs</c>
/// and are discovered via the <c>IApplyConfigurationsFromAssembly</c> call
/// below — new entities only need to drop a configuration class in that
/// folder to be wired up.
/// </summary>
public class KitchenDbContext(DbContextOptions<KitchenDbContext> options)
    : DbContext(options), IUnitOfWork, IOutboxDbContext
{
    public DbSet<KitchenTicket> Tickets => Set<KitchenTicket>();
    public DbSet<KitchenTicketItem> TicketItems => Set<KitchenTicketItem>();
    public DbSet<KitchenStation> Stations => Set<KitchenStation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<OutboxDeadMessage> OutboxDeadMessages => Set<OutboxDeadMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KitchenDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxDeadMessageConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
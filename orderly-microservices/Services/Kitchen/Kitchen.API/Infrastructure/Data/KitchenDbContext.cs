namespace Kitchen.API.Infrastructure.Data;

/// <summary>
/// Kitchen's relational store. Owns three DbSets (Tickets, Items, Stations)
/// backing the domain aggregates. Configurations live in
/// <c>Configurations/*Configuration.cs</c> and are discovered via the
/// <c>IApplyConfigurationsFromAssembly</c> call below — new entities only
/// need to drop a configuration class in that folder to be wired up.
/// </summary>
public class KitchenDbContext(DbContextOptions<KitchenDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<KitchenTicket> Tickets => Set<KitchenTicket>();
    public DbSet<KitchenTicketItem> TicketItems => Set<KitchenTicketItem>();
    public DbSet<KitchenStation> Stations => Set<KitchenStation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KitchenDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Multitenancy;

public static class TenantQueryFilterExtensions
{
    public static void ApplyTenantFilter<TEntity>(
        this ModelBuilder modelBuilder,
        Func<Guid> getRestaurantId)
        where TEntity : class, ITenantEntity
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => e.RestaurantId == getRestaurantId());
    }
}

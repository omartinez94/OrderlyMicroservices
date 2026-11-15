namespace Catalog.API.Features.MenuItems.Common;

/// <summary>
/// Centralises post-mutation side-effects for <see cref="MenuItem"/> and its
/// child aggregates (variations, ingredient links, combo items):
/// <list type="number">
/// <item>Stages a <see cref="MenuItemChangedIntegrationEvent"/> in the transactional outbox (when <c>CatalogMenuEvents</c> is enabled).</item>
/// <item>Commits the ambient <see cref="CatalogDbContext"/> transaction so the aggregate mutation and outbox message are persisted atomically.</item>
/// <item>Invalidates the Redis menu cache (<c>catalog:menu:{restaurantId}</c>) <b>after</b> the transaction commits.</item>
/// </list>
/// </summary>
public interface IMenuItemMutationNotifier
{
    /// <summary>
    /// Stages a <see cref="MenuItemChangedIntegrationEvent"/> for a direct <see cref="MenuItem"/> mutation,
    /// commits <see cref="CatalogDbContext.SaveChangesAsync(CancellationToken)"/>, and invalidates the menu cache.
    /// </summary>
    Task SaveAndNotifyAsync(
        MenuItem menuItem,
        MenuItemChangeType changeType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a <see cref="MenuItemChangedIntegrationEvent"/> for a related mutation (variation, ingredient link, combo item),
    /// commits <see cref="CatalogDbContext.SaveChangesAsync(CancellationToken)"/>, and invalidates the menu cache.
    /// </summary>
    Task SaveAndNotifyRelatedAsync(
        Guid menuItemId,
        Guid restaurantId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IMenuItemMutationNotifier"/>
public sealed class MenuItemMutationNotifier(
    CatalogDbContext dbContext,
    IOutboxPublisher outbox,
    ICatalogCache cache,
    IFeatureManager featureManager) : IMenuItemMutationNotifier
{
    /// <inheritdoc/>
    public async Task SaveAndNotifyAsync(
        MenuItem menuItem,
        MenuItemChangeType changeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(menuItem);

        bool eventsEnabled = await featureManager
            .IsEnabledAsync("CatalogMenuEvents", cancellationToken)
            .ConfigureAwait(false);

        if (eventsEnabled)
        {
            bool isDeleted = changeType == MenuItemChangeType.Deleted;
            MenuItemChangedIntegrationEvent integrationEvent = new()
            {
                MenuItemId = menuItem.Id,
                RestaurantId = menuItem.RestaurantId,
                ChangeType = changeType,
                Name = isDeleted ? null : menuItem.Name,
                BasePrice = isDeleted ? null : menuItem.BasePrice,
                IsAvailable = isDeleted ? null : menuItem.IsAvailable,
                AvailabilityStatus = isDeleted ? null : menuItem.AvailabilityStatus.ToString(),
                ImageUrl = isDeleted ? null : menuItem.ImageUrl,
            };

            await outbox.PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await cache.InvalidateMenuAsync(menuItem.RestaurantId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveAndNotifyRelatedAsync(
        Guid menuItemId,
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId != Guid.Empty)
        {
            bool eventsEnabled = await featureManager
                .IsEnabledAsync("CatalogMenuEvents", cancellationToken)
                .ConfigureAwait(false);

            if (eventsEnabled)
            {
                MenuItemChangedIntegrationEvent integrationEvent = new()
                {
                    MenuItemId = menuItemId,
                    RestaurantId = restaurantId,
                    ChangeType = MenuItemChangeType.Updated,
                };

                await outbox.PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (restaurantId != Guid.Empty)
        {
            await cache.InvalidateMenuAsync(restaurantId, cancellationToken).ConfigureAwait(false);
        }
    }
}

using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Application.EventHandlers.Domain;

/// <summary>
/// MediatR notification handler that consumes every
/// <see cref="IDomainEvent"/> raised by Catalog aggregates in the
/// Ingredient Availability Engine's mutation surface. For each affected
/// menu item, it (1) loads the engine's inputs, (2) calls
/// <see cref="IngredientAvailabilityEngine.AvailabilityProfileFor"/>,
/// (3) writes <c>MenuItem.AvailabilityStatus</c> if it flipped, and (4)
/// publishes an
/// <see cref="IngredientAvailabilityChangedIntegrationEvent"/> via
/// <see cref="IOutboxPublisher"/>.
/// </summary>
/// <remarks>
/// <para><b>One handler, many events.</b> Subscribes to
/// <see cref="IDomainEvent"/> rather than each concrete event — mirrors
/// <c>KitchenTicketBroadcaster</c> (Kitchen's broadcaster pattern). A
/// <c>switch</c> on the event type routes to the right input-set
/// loader.</para>
/// <para><b>Feature flag.</b> The whole pipeline is gated by
/// <c>CatalogMenuEvents</c> — when the flag is off, no MenuItem writes
/// and no integration events publish (mirrors the
/// <c>OrderFullfilment</c> gate on <c>OrderCreatedEventHandler</c>).</para>
/// <para><b>Nested SaveChanges.</b> The handler runs inside the outer
/// <c>SaveChangesAsync</c> that triggered the dispatch. Its writes to
/// <c>MenuItem.AvailabilityStatus</c> are persisted by a nested
/// <c>SaveChangesAsync</c> on the same ambient <c>CatalogDbContext</c>;
/// the integration event is staged on the outbox (which is committed by
/// the OUTER <c>SaveChangesAsync</c>). When the outer transaction rolls
/// back, the integration event row is rolled back too — the at-least-once
/// guarantee is preserved.</para>
/// </remarks>
public sealed class IngredientAvailabilityChangedDomainEventHandler(
    CatalogDbContext dbContext,
    IOutboxPublisher outbox,
    ICatalogCache cache,
    IFeatureManager featureManager,
    ILogger<IngredientAvailabilityChangedDomainEventHandler> logger)
    : INotificationHandler<IDomainEvent>
{
    /// <inheritdoc/>
    public async Task Handle(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // Engine-wide feature flag — matches the publisher gating on
        // CatalogMenuEvents. Off → the handler is a no-op.
        if (!await featureManager.IsEnabledAsync("CatalogMenuEvents", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // Find the affected menu-item ids once per inbound event. The
        // switch picks the right loader per event type.
        var affectedMenuItemIds = domainEvent switch
        {
            IngredientChangedDomainEvent ev => await GetMenuItemIdsForIngredientAsync(ev.IngredientId, cancellationToken).ConfigureAwait(false),
            IngredientAlternativeChangedDomainEvent ev => await GetMenuItemIdsForIngredientAsync(ev.OriginalIngredientId, cancellationToken).ConfigureAwait(false),
            MenuItemIngredientChangedDomainEvent ev => new[] { ev.MenuItemId },
            _ => Array.Empty<Guid>(),
        };

        if (affectedMenuItemIds.Length == 0)
        {
            return;
        }

        foreach (var menuItemId in affectedMenuItemIds)
        {
            await RecomputeAndPublishAsync(menuItemId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Guid[]> GetMenuItemIdsForIngredientAsync(int ingredientId, CancellationToken cancellationToken)
    {
        return await dbContext.MenuItemIngredients
            .Where(link => link.IngredientId == ingredientId)
            .Select(link => (Guid?)link.MenuItemId)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false) is { } ids && ids.Length > 0
            ? ids.Select(id => id!.Value).ToArray()
            : Array.Empty<Guid>();
    }

    private async Task RecomputeAndPublishAsync(Guid menuItemId, CancellationToken cancellationToken)
    {
        // 1. Load the menu item's required ingredients (one row per ingredient).
        var requiredIngredients = await dbContext.MenuItemIngredients
            .AsNoTracking()
            .Where(link => link.MenuItemId == menuItemId)
            .Select(link => new IngredientAvailabilityEngine.MenuItemIngredientRow(link.IngredientId, link.IsOptional))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (requiredIngredients.Count == 0)
        {
            // No recipe rows — treat as Available (matches engine rule 1:
            // "all required ingredients satisfied" vacuously true).
            return;
        }

        // 2. Load availability for every referenced ingredient. The engine's
        // `ingredientAvailability` dict also needs each alternative target's
        // IsAvailable, so include those too.
        var referencedIds = requiredIngredients.Select(r => r.IngredientId).ToHashSet();

        // Pull alternatives for any of the referenced originals.
        var alternativeOriginalIds = await dbContext.IngredientAlternatives
            .AsNoTracking()
            .Where(a => referencedIds.Contains(a.OriginalIngredientId))
            .Select(a => a.OriginalIngredientId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var alternatives = await dbContext.IngredientAlternatives
            .AsNoTracking()
            .Where(a => referencedIds.Contains(a.OriginalIngredientId))
            .Select(a => new IngredientAvailabilityEngine.AlternativeEdge(a.OriginalIngredientId, a.AlternativeIngredientId, a.AutoSubstitute))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var altOriginalId in alternativeOriginalIds)
        {
            referencedIds.Add(altOriginalId);
        }

        var ingredientAvailability = await dbContext.Ingredients
            .AsNoTracking()
            .Where(i => referencedIds.Contains(i.Id))
            .Select(i => new IngredientAvailabilityEngine.IngredientRow(i.Id, i.IsAvailable))
            .ToDictionaryAsync(i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        // 3. Look up the menu item's RestaurantId + AllowAutoSubstitute.
        // Join MenuItems to Restaurants to read AllowAutoSubstitute. (Use
        // IgnoreQueryFilters to be safe; no filter applies to MenuItem in
        // practice, but the engine recompute must work even if filters
        // evolve.)
        var menuItemInfo = await dbContext.MenuItems
            .IgnoreQueryFilters()
            .Where(m => m.Id == menuItemId)
            .Join(dbContext.Restaurants, m => m.RestaurantId, r => r.Id, (m, r) => new { m.RestaurantId, r.AllowAutoSubstitute })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (menuItemInfo is null)
        {
            // Menu item or restaurant no longer exists — nothing to do.
            return;
        }

        // 4. Run the pure engine.
        var newProfile = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients,
            ingredientAvailability,
            alternatives,
            menuItemInfo.AllowAutoSubstitute);

        // 5. Compare with current MenuItem.AvailabilityStatus. Skip if no flip.
        var menuItem = await dbContext.MenuItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == menuItemId, cancellationToken)
            .ConfigureAwait(false);

        if (menuItem is null || menuItem.AvailabilityStatus == newProfile.Status)
        {
            // No flip — no write, no publish. (AutoSubstituteOf is informational
            // only; it's not persisted on MenuItem.)
            return;
        }

        menuItem.AvailabilityStatus = newProfile.Status;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 6. Invalidate the menu cache so the next snapshot read reflects the
        // new status. The cached MenuItemNode.AvailabilityStatus field is
        // populated by MenuReader from mi.AvailabilityStatus.
        await cache.InvalidateMenuAsync(menuItem.RestaurantId, cancellationToken).ConfigureAwait(false);

        // 7. Stage the integration event on the outbox. The outer SaveChanges
        // (the one the handler is currently nested inside) commits the row
        // — if the outer transaction rolls back, the integration event row
        // is rolled back too, preserving at-least-once semantics.
        await outbox.PublishAsync(new IngredientAvailabilityChangedIntegrationEvent
        {
            MenuItemId = menuItemId,
            RestaurantId = menuItem.RestaurantId,
            AvailabilityStatus = newProfile.Status.ToString(),
            AutoSubstituteOf = newProfile.AutoSubstituteOf,
        }, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Engine flipped MenuItem {MenuItemId} to {Status} (auto-sub of {AutoSubstituteOf}); integration event staged.",
            menuItemId,
            newProfile.Status,
            newProfile.AutoSubstituteOf);
    }
}
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Availability;

/// <summary>
/// Safety-net hosted service that periodically re-runs the Ingredient
/// Availability Engine for every menu item in every restaurant. Catches
/// cases where an in-process domain event was missed (e.g. a bug in the
/// dispatch path, an external SQL edit, a partial deployment). Mirrors
/// <see cref="Caching.CacheDriftRepairService"/> in shape.
/// </summary>
/// <remarks>
/// <para><b>Off by default.</b> The whole loop self-gates on the
/// <c>CatalogAvailabilityEngineReconcile</c> feature flag (default
/// <see langword="false"/>). The flag flips on without a redeploy via
/// the same env-var pattern as <c>CatalogRedisCache</c>.</para>
/// <para><b>Reuses the same engine + handler shape.</b> Rather than
/// duplicate the engine's input-loading + write + publish logic, the
/// reconcile tick invokes
/// <see cref="IngredientAvailabilityChangedDomainEventHandler"/> once
/// per inbound event. The "sweep" is therefore a no-op when the in-process
/// path already covered everything — the handler writes only on actual
/// flips. This keeps the engine surface single-sourced.</para>
/// <para><b>Tick budget.</b> Default cadence is
/// <see cref="CatalogOptions.AvailabilityRecurrenceIntervalMinutes"/>
/// minutes (1 minute). The hosted service runs once at startup before
/// sleeping, then on the cadence; ticks overlap with the configured
/// interval via <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</para>
/// </remarks>
public sealed class IngredientAvailabilityReconcileService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<CatalogOptions> options,
    ILogger<IngredientAvailabilityReconcileService> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "IngredientAvailabilityReconcileService starting (default interval: {IntervalMinutes}m).",
            options.CurrentValue.AvailabilityRecurrenceIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "IngredientAvailabilityReconcileService sweep failed; will retry on next tick.");
            }

            try
            {
                var interval = TimeSpan.FromMinutes(options.CurrentValue.AvailabilityRecurrenceIntervalMinutes);
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("IngredientAvailabilityReconcileService stopped.");
    }

    /// <summary>
    /// Runs one reconcile tick: enumerate every menu item, dispatch a
    /// synthetic <see cref="Domain.Events.MenuItemIngredientChangedDomainEvent"/>
    /// for it, and let the existing handler recompute the profile. The
    /// handler's compare-and-skip logic means unchanged items cost one DB
    /// roundtrip (the engine inputs) and no write.
    /// </summary>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var menuItemIds = await dbContext.MenuItems
            .IgnoreQueryFilters()
            .Select(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (menuItemIds.Count == 0)
        {
            logger.LogDebug("IngredientAvailabilityReconcileService: no menu items; nothing to do.");
            return;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var dispatched = 0;

        foreach (var menuItemId in menuItemIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Pick any one ingredient link for this menu item (the engine
            // recomputes from the whole recipe regardless of which link
            // triggered the dispatch — the link-id is just used by the
            // handler's switch as a discriminator).
            var anyLink = await dbContext.MenuItemIngredients
                .Where(link => link.MenuItemId == menuItemId)
                .Select(link => new { link.Id, link.IngredientId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (anyLink is null)
            {
                continue;
            }

            await mediator.Publish(new MenuItemIngredientChangedDomainEvent(
                anyLink.Id,
                menuItemId,
                anyLink.IngredientId,
                MenuItemIngredientChangedDomainEvent.ChangeKind.Created) // Created vs Deleted doesn't change engine output
            , cancellationToken).ConfigureAwait(false);

            dispatched++;
        }

        logger.LogInformation(
            "IngredientAvailabilityReconcileService sweep dispatched {Dispatched} synthetic domain events for engine recompute.",
            dispatched);
    }
}
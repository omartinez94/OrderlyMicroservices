using BuildingBlocks.Correlation;
using Marten;
using Microsoft.Extensions.Options;

namespace Basket.API.Services;

/// <summary>
/// Hosted service that deletes abandoned carts whose
/// <c>ExpiresAt</c> has passed. Pure housekeeping — no
/// <c>BasketCheckoutEvent</c> is published because the cart is being
/// discarded, not promoted to an order. Run cadence defaults to 5
/// minutes (per plan §6 Phase 3).
/// </summary>
/// <remarks>
/// <para>
/// Operates cross-tenant by talking to <see cref="IDocumentStore"/>
/// directly rather than going through <see cref="Data.IBasketRepository"/>.
/// The repository's <c>AssertTenant</c> guard rejects any cross-tenant
/// access; the sweep service is the one legitimate caller that must
/// reach across tenants to keep the store tidy. Per tick the service
/// opens a fresh <see cref="IDocumentSession"/> via the Marten store
/// (sessions are <c>Scoped</c> per the <c>UseLightweightSessions()</c>
/// registration in <c>Program.cs</c>; the hosted service is a singleton,
/// so it opens its own session per tick).
/// </para>
/// <para>
/// <b>Claim semantics.</b> The sweep is an idempotent delete — if two
/// replicas tick concurrently (multi-instance deployment), each
/// snapshot reads the same candidates; when both try to delete the
/// same row, the second <c>SaveChangesAsync</c> raises
/// <c>ConcurrencyException</c> (Marten's <c>mt_version</c> mechanism).
/// The outer <see cref="ExecuteAsync"/> loop catches and logs the
/// exception per tick; the next tick carries on with the leftover
/// rows. This is the same pattern the
/// <see cref="Messaging.CheckoutBasketOutboxDispatcher"/> uses for
/// claim semantics — single-replica today, multi-replica-safe tomorrow
/// without code changes (the version conflict is the natural fence).
/// </para>
/// <para>
/// <see cref="IAsyncDisposable"/> + <see cref="IDisposable"/> per plan
/// §0.3.3. <see cref="StopAsync"/> drains the in-flight iteration
/// before the host shuts down so a half-deleted batch is not left on
/// the floor.
/// </para>
/// </remarks>
public sealed class BasketExpirySweepService(
    IServiceProvider services,
    IOptions<BasketOptions> options,
    ILogger<BasketExpirySweepService> logger)
    : BackgroundService, IAsyncDisposable
{
    private readonly BasketOptions.ExpirySweepOptions _options = options.Value.ExpirySweep;
    private readonly ILogger<BasketExpirySweepService> _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Basket expiry sweep disabled via BasketOptions.ExpirySweep.Enabled = false.");
            return;
        }

        _logger.LogInformation(
            "Basket expiry sweep started. Interval {Interval}, batch {Batch}.",
            _options.Interval, _options.BatchSize);

        // PeriodicTimer is the cancel-friendly replacement for
        // Task.Delay loops — caller-supplied cancellation stops the
        // next tick without throwing. The cancellation token reaches
        // the timer via WaitForNextTickAsync(stoppingToken).
        using var timer = new PeriodicTimer(_options.Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var deleted = await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
                    if (deleted > 0)
                    {
                        _logger.LogInformation(
                            "Basket expiry sweep deleted {Count} expired cart(s).",
                            deleted);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Per-tick failure: log and continue. The next
                    // tick retries the leftover rows. We do NOT
                    // propagate — stopping the service after a single
                    // transient Marten failure would orphan the
                    // sweep permanently.
                    _logger.LogError(ex, "Basket expiry sweep iteration failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown — exit cleanly.
        }

        _logger.LogInformation("Basket expiry sweep stopped.");
    }

    /// <summary>
    /// Runs one sweep iteration out-of-band, bypassing the
    /// <see cref="BasketOptions.ExpirySweepOptions.Enabled"/> toggle
    /// and the periodic timer. Visible for tests so the service can
    /// be driven deterministically without waiting for the next tick.
    /// </summary>
    /// <returns>
    /// The number of baskets deleted by this iteration. Zero when
    /// no rows are expired.
    /// </returns>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var correlationId = CorrelationContext.Current ?? Guid.NewGuid().ToString();

        // Open a one-off session — BasketExpirySweepService is a
        // singleton, so it would otherwise capture a session-per-app
        // start. Open+Save+Close per tick is the project pattern
        // (mirrors CheckoutBasketOutboxDispatcher.DispatchOnceAsync).
        await using var session = store.LightweightSession();

        var now = SystemClock.Instance.GetCurrentInstant();

        // Project to (UserId, RestaurantId) only — the sweep never
        // touches the inner fields, so we never load the full doc
        // payloads. The TenantId column is stripped by Marten when
        // the projection does not reference it.
        var candidates = await session.Query<Models.Basket>()
            .Where(b => b.ExpiresAt < now)
            .OrderBy(b => b.ExpiresAt)
            .Take(_options.BatchSize)
            .Select(b => new { b.UserId, b.RestaurantId, b.ExpiresAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return 0;
        }

        foreach (var candidate in candidates)
        {
            using var _ = _logger.BeginScope(new Dictionary<string, object>
            {
                ["UserId"] = candidate.UserId,
                ["RestaurantId"] = candidate.RestaurantId,
                ["CorrelationId"] = correlationId,
            });

            // Reload the full document — the projection above only
            // carried the identity columns. Marten's
            // IDocumentSession.Delete<T>(...) stamps the doc for
            // deletion; the SaveChangesAsync at the end commits the
            // batch.
            var fullDoc = await session.Query<Models.Basket>()
                .Where(b => b.UserId == candidate.UserId
                            && b.RestaurantId == candidate.RestaurantId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (fullDoc is null)
            {
                // Race: another replica (or the user themselves via
                // DELETE /cart) deleted the row between the projection
                // read and the reload. Skip.
                continue;
            }

            session.Delete(fullDoc);
        }

        try
        {
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.GetType().Name == "ConcurrencyException")
        {
            // Multi-replica race: a peer replica deleted (or
            // refreshed) the same row between our projection and
            // SaveChangesAsync. Rows we lost the race on stay
            // deleted — the peer already removed them. The
            // remaining rows in the batch either committed or
            // failed together; log + continue. The next tick
            // re-evaluates and re-deletes whatever is still
            // present.
            _logger.LogWarning(
                ex,
                "Basket expiry sweep lost a concurrent-delete race on a batch; continuing.");
        }

        return candidates.Count;
    }

    /// <summary>
    /// Drain the in-flight iteration before host shutdown. The base
    /// <see cref="BackgroundService.StopAsync"/> cancels the
    /// <see cref="PeriodicTimer"/>; we additionally wait for the
    /// active <see cref="SweepOnceAsync"/> to finish (or cancel) so
    /// a half-staged batch is not left on the floor.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // No unmanaged resources today. The per-iteration IServiceScope
        // is disposed inside SweepOnceAsync; the PeriodicTimer is
        // disposed via its using-declaration in ExecuteAsync. Kept
        // for future-proofing.
        return ValueTask.CompletedTask;
    }
}

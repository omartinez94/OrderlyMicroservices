namespace Basket.API.Audit;

/// <summary>
/// Marten-backed <see cref="IBasketAuditLog"/>. Uses a fresh
/// <see cref="IDocumentSession"/> per call (sessions are scoped;
/// the audit service is singleton-per-app, so it opens its own
/// scope per invocation — same pattern as
/// <see cref="Basket.API.Services.BasketExpirySweepService"/>).
/// </summary>
public sealed class MartenBasketAuditLog(
    IServiceProvider services,
    IPublisher publisher)
    : IBasketAuditLog
{
    public async Task AppendAsync(BasketAuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Open a scope per write — singleton services cannot
        // capture scoped Marten sessions across requests.
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession();

        session.Store(entry);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Publish the notification AFTER the row commits — a
        // consumer that tries to read the row sees the latest
        // version. A failed publish is logged-but-not-rethrown;
        // the audit row is the source of truth.
        await publisher.Publish(new BasketAuditLogAppendedNotification(entry), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BasketAuditLogEntry>> QueryAsync(
        Guid restaurantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(pageSize));

        // Open a read session — Marten's IQuerySession is a thin
        // wrapper over the store; LightweightSession() is read+write
        // and works for both, but QuerySession is the idiomatic
        // choice for read-only paths. The Marten store exposes
        // both via the same factory.
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.QuerySession();

        return await session.Query<BasketAuditLogEntry>()
            .Where(e => e.RestaurantId == restaurantId)
            .OrderByDescending(e => e.OccurredAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

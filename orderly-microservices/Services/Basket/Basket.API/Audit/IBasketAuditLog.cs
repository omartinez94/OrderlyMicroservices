namespace Basket.API.Audit;

/// <summary>
/// Append-only audit log for cross-account basket mutations. The
/// admin endpoints (Phase 4) call this on every successful mutation;
/// user-facing endpoints do NOT — the audit row is for support /
/// compliance review, not for the user's own history.
/// </summary>
public interface IBasketAuditLog
{
    /// <summary>
    /// Append a single audit row. Implementation should batch by
    /// Marten session (a single <c>SaveChangesAsync</c>) so
    /// admin-bulk paths don't pay an N-round-trip penalty.
    /// </summary>
    Task AppendAsync(BasketAuditLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paged audit query — newest-first, tenant-scoped by
    /// <paramref name="restaurantId"/>. Used by the (planned)
    /// <c>GET /api/v1/admin/audit</c> endpoint; not part of the
    /// Phase 4 admin-carts surface but the interface supports it
    /// so a future plan does not have to widen the contract.
    /// </summary>
    Task<IReadOnlyList<BasketAuditLogEntry>> QueryAsync(
        Guid restaurantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// MediatR notification published after every successful
/// <see cref="IBasketAuditLog.AppendAsync"/>. Future cross-service
/// consumers (Notification v1 "compliance email" hook, etc.) can
/// subscribe without changing the admin endpoints' write path.
/// </summary>
public record BasketAuditLogAppendedNotification(BasketAuditLogEntry Entry) : INotification;

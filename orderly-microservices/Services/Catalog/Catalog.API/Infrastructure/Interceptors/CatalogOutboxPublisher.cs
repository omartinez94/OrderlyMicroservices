using BuildingBlocks.Messaging.Outbox;
using Catalog.API.Data;

namespace Catalog.API.Infrastructure.Interceptors;

/// <summary>
/// Catalog-side <see cref="OutboxPublisher{TContext}"/> specialization. The
/// handler injects <see cref="IOutboxPublisher"/>; this concrete class is
/// also registered so tests and edge callers can resolve the typed instance
/// directly (mirrors <c>Ordering.Infrastructure</c>).
/// </summary>
public sealed class CatalogOutboxPublisher(CatalogDbContext context)
    : OutboxPublisher<CatalogDbContext>
{
    /// <inheritdoc/>
    protected override CatalogDbContext ResolveContext() => context;
}
using BuildingBlocks.Messaging.Outbox;
using Catalog.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Catalog.API.Infrastructure.Interceptors;

/// <summary>
/// Postgres-flavored <see cref="OutboxDispatcher{TContext}"/> for Catalog.
/// Claims pending <c>outbox_messages</c> rows with
/// <c>FOR UPDATE SKIP LOCKED</c> so multiple replicas can run in parallel
/// without double-publishing (per <c>CATALOG_SERVICE_PLAN.md</c> §8
/// cross-service coordination rules).
/// </summary>
/// <remarks>
/// The Postgres dialect is copied verbatim from
/// <c>Kitchen.API/Infrastructure/Interceptors/KitchenOutboxDispatcher.cs</c>
/// — Kitchen already exercises the <c>FOR UPDATE SKIP LOCKED</c> pattern on
/// the same engine, so Catalog mirrors the proven shape (no need to invent).
/// </remarks>
public sealed class CatalogOutboxDispatcher(
    IServiceProvider services,
    IOptions<OutboxOptions> options,
    ILogger<CatalogOutboxDispatcher> logger)
    : OutboxDispatcher<CatalogDbContext>(services, options, logger)
{
    /// <summary>
    /// Builds a fresh <see cref="CatalogDbContext"/> per dispatcher tick.
    /// Required because <see cref="OutboxDispatcher{TContext}"/> resolves
    /// the context outside the request scope (the dispatcher is a singleton
    /// hosted service; <see cref="CatalogDbContext"/> is scoped).
    /// </summary>
    protected override CatalogDbContext CreateContext(IServiceProvider services)
    {
        var optionsAccessor = services.GetRequiredService<DbContextOptions<CatalogDbContext>>();
        return new CatalogDbContext(optionsAccessor);
    }

    /// <summary>
    /// Postgres-native row-claim SQL: <c>SELECT … FOR UPDATE SKIP LOCKED</c>
    /// on the live <c>outbox_messages</c> rows whose <c>DispatchedAt</c> is
    /// <c>NULL</c>. Quoted PascalCase identifiers match the column casing
    /// EF Core emits (Postgres folds unquoted identifiers to lowercase).
    /// </summary>
    protected override FormattableString BuildClaimSql(int batchSize) =>
        $@"SELECT *
           FROM outbox_messages
           WHERE ""DispatchedAt"" IS NULL
           ORDER BY ""OccurredOn""
           LIMIT {batchSize}
           FOR UPDATE SKIP LOCKED";
}
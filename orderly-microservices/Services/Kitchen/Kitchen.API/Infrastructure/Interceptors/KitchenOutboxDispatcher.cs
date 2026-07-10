using BuildingBlocks.Messaging.Outbox;
using Kitchen.API.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kitchen.API.Infrastructure.Interceptors;

/// <summary>
/// Kitchen-side implementation of <see cref="OutboxDispatcher{TContext}"/>.
/// Spawns a fresh <see cref="KitchenDbContext"/> per poll iteration so a
/// broker publish failure can be retried on the next tick without
/// poisoning the caller's scope.
///
/// Row-claim strategy (Postgres):
/// <c>SELECT ... FOR UPDATE SKIP LOCKED</c> — Postgres-specific syntax
/// that locks claimed rows for the duration of the surrounding
/// transaction and skips rows already locked by a different
/// transaction. Combined with the explicit transaction in
/// <see cref="OutboxDispatcher{TContext}"/>, this guarantees that two
/// replicas picking up the same outbox row can't both publish.
/// </summary>
public class KitchenOutboxDispatcher(
    IServiceProvider services,
    IOptions<OutboxOptions> options,
    ILogger<KitchenOutboxDispatcher> logger)
    : OutboxDispatcher<KitchenDbContext>(services, options, logger)
{
    protected override KitchenDbContext CreateContext(IServiceProvider services)
    {
        var optionsAccessor = services.GetRequiredService<
            Microsoft.EntityFrameworkCore.DbContextOptions<KitchenDbContext>>();
        return new KitchenDbContext(optionsAccessor);
    }

    protected override FormattableString BuildClaimSql(int batchSize) =>
        $@"SELECT *
           FROM outbox_messages
           WHERE ""DispatchedAt"" IS NULL
           ORDER BY ""OccurredOn""
           LIMIT {batchSize}
           FOR UPDATE SKIP LOCKED";
}

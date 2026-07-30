using BuildingBlocks.Messaging.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ordering.Infrastructure.Data.Interceptors;

/// <summary>
/// Ordering-side implementation of <see cref="OutboxDispatcher{TContext}"/>.
/// Spawns a fresh <see cref="ApplicationDBContext"/> per poll iteration
/// so a broker publish failure can be retried on the next tick without
/// poisoning the caller's scope.
///
/// Row-claim strategy (MSSQL):
/// <c>SELECT TOP (@batch) ... WITH (ROWLOCK, UPDLOCK, READPAST)</c> —
/// acquires an update lock on each claimed row and skips rows already
/// locked by a different session. Combined with the explicit
/// transaction in <see cref="OutboxDispatcher{TContext}"/>, this
/// guarantees that two replicas picking up the same outbox row can't
/// both publish.
/// </summary>
public class OrderingOutboxDispatcher(
    IServiceProvider services,
    IOptions<OutboxOptions> options,
    ILogger<OrderingOutboxDispatcher> logger)
    : OutboxDispatcher<ApplicationDBContext>(services, options, logger), IOrderingOutboxRunner
{
    protected override ApplicationDBContext CreateContext(IServiceProvider services)
    {
        // Each iteration gets a fresh DbContext — keyed by the
        // DbContextOptions the host already registered.
        var optionsAccessor = services.GetRequiredService<
            Microsoft.EntityFrameworkCore.DbContextOptions<ApplicationDBContext>>();
        return new ApplicationDBContext(optionsAccessor);
    }

    protected override FormattableString BuildClaimSql(int batchSize) =>
        $@"SELECT TOP ({batchSize}) *
           FROM outbox_messages WITH (ROWLOCK, UPDLOCK, READPAST)
           WHERE [DispatchedAt] IS NULL
           ORDER BY [OccurredOn]";
}

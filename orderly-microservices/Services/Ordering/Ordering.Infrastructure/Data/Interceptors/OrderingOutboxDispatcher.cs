using BuildingBlocks.Messaging.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ordering.Infrastructure.Data.Interceptors;

/// <summary>
/// Ordering-side implementation of <see cref="OutboxDispatcher{TContext}"/>.
/// Spawns a fresh <see cref="ApplicationDBContext"/> per poll iteration
/// so a broker publish failure can be retried on the next tick without
/// poisoning the caller's scope.
/// </summary>
public class OrderingOutboxDispatcher(
    IServiceProvider services,
    IOptions<OutboxOptions> options,
    ILogger<OrderingOutboxDispatcher> logger)
    : OutboxDispatcher<ApplicationDBContext>(services, options, logger)
{
    protected override ApplicationDBContext CreateContext(IServiceProvider services)
    {
        // Each iteration gets a fresh DbContext — keyed by the
        // DbContextOptions the host already registered.
        var optionsAccessor = services.GetRequiredService<
            Microsoft.EntityFrameworkCore.DbContextOptions<ApplicationDBContext>>();
        return new ApplicationDBContext(optionsAccessor);
    }
}
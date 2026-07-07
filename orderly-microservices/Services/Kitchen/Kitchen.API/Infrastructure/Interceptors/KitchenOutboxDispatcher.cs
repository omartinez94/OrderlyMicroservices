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
}
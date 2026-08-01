using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ordering.Infrastructure.Data.Interceptors;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Integration coverage for the F.3 multi-replica outbox row-claim.
///
/// Two <see cref="OrderingOutboxDispatcher"/> instances run in parallel
/// against the same MSSQL + RabbitMQ, each claiming rows with
/// <c>WITH (ROWLOCK, UPDLOCK, READPAST)</c>. The combination of the
/// engine-native lock hint + the explicit transaction that surrounds the
/// claim/publish/stamp cycle guarantees that two replicas contending on
/// the same row publish the row exactly once.
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderingOutboxMultiReplicaTests
{
    private readonly OrderingWebApplicationFactory _factory;

    public OrderingOutboxMultiReplicaTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ParallelDispatchers_EachRowClaimedExactlyOnce()
    {
        // Stage N outbox rows directly via the publisher. The publisher
        // writes rows to outbox_messages inside the ambient DbContext
        // save transaction; after this loop we have N rows with
        // DispatchedAt == null ready to be claimed.
        const int rowCount = 10;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            await dbContext.OutboxMessages.ExecuteDeleteAsync();

            var publisher = scope.ServiceProvider
                .GetRequiredService<IOutboxPublisher>();
            for (var i = 0; i < rowCount; i++)
            {
                await publisher.PublishAsync(
                    new OutboxCountingIntegrationEvent($"payload-{i}"),
                    CancellationToken.None);
            }

            // Make sure the staged rows flush to disk before we race
            // the dispatchers against them.
            await scope.ServiceProvider.GetRequiredService<ApplicationDBContext>()
                .SaveChangesAsync();
        }

        // Two dispatcher instances, each with its own options + logger
        // but resolved from the same IServiceProvider so they hit the
        // same MSSQL + RabbitMQ backing stores.
        var options = Options.Create(new OutboxOptions
        {
            Enabled = true,            // bypass the host's toggle
            BatchSize = 100,
            ActivePollInterval = TimeSpan.FromMilliseconds(50),
            IdlePollInterval = TimeSpan.FromMilliseconds(50),
        });

        var dispatcherA = new OrderingOutboxDispatcher(
            _factory.Services, options, NullLogger<OrderingOutboxDispatcher>.Instance);
        var dispatcherB = new OrderingOutboxDispatcher(
            _factory.Services, options, NullLogger<OrderingOutboxDispatcher>.Instance);

        // Race the two — the first to acquire the locks sees the
        // unclaimed rows; the second sees an empty result set because
        // the lock + WHERE DispatchedAt IS NULL combine to skip rows
        // the first dispatcher holds.
        var tA = Task.Run(() => dispatcherA.DispatchOnceAsync(CancellationToken.None));
        var tB = Task.Run(() => dispatcherB.DispatchOnceAsync(CancellationToken.None));
        var dispatched = await Task.WhenAll(tA, tB);
        var totalDispatched = dispatched.Sum();

        // Exactly one row per outbox row, no duplicates.
        totalDispatched.Should().Be(rowCount);

        // Every row is stamped.
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
        var unstamped = await db.OutboxMessages
            .Where(m => m.DispatchedAt == null)
            .CountAsync();
        unstamped.Should().Be(0);
    }
}

/// <summary>
/// Carrier integration event for the multi-replica test. The
/// MassTransit bus publishes it through the outbox dispatcher; no
/// consumer is required for the assertion (the test stops at the
/// "exactly one dispatch" assertion).
/// </summary>
public record OutboxCountingIntegrationEvent(string Payload) : IntegrationEvent;

using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using Ordering.Infrastructure.Data.Interceptors;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// The outbox dispatcher routes rows whose
/// <see cref="OutboxMessage.SchemaVersion"/> exceeds
/// <see cref="OutboxOptions.MaxSupportedVersion"/> to the
/// <c>outbox_messages_dead</c> poison table instead of publishing them
/// to RabbitMQ. This test stages one such row, runs the dispatcher, and
/// asserts the row is moved (not copied + left behind) with the
/// expected reason.
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderingOutboxDeadLetterTests
{
    private readonly OrderingWebApplicationFactory _factory;

    public OrderingOutboxDeadLetterTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FutureVersionRow_IsMovedToDeadTable()
    {
        // Stage one outbox row stamped with a future schema version
        // that the dispatcher explicitly cannot route. We have to drop
        // down to the DbContext to set SchemaVersion — the publisher
        // always stamps 1 today.
        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOn = SystemClock.Instance.GetCurrentInstant(),
            Type = typeof(OutboxCountingIntegrationEvent).FullName!,
            Payload = "{\"Id\":\"" + Guid.NewGuid() + "\",\"OccurredOn\":\"2026-07-10T00:00:00Z\",\"EventType\":\"...\",\"Payload\":\"future\"}",
            SchemaVersion = 99,
        };

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            db.OutboxMessages.Add(row);
            await db.SaveChangesAsync();
        }

        // Run a single dispatcher iteration. MaxSupportedVersion stays
        // at its default (1) — way below the row's stamped 99, so the
        // schema-version gate must fire and route the row to the
        // poison table.
        var options = Options.Create(new OutboxOptions
        {
            Enabled = true,
            BatchSize = 100,
            MaxSupportedVersion = 1,
        });
        var dispatcher = new OrderingOutboxDispatcher(
            _factory.Services, options, NullLogger<OrderingOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchOnceAsync(CancellationToken.None);

        // The dispatcher did not publish — its return value counts only
        // the broker-relay rows.
        dispatched.Should().Be(0);

        // The row is no longer in the live table.
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var dbVerify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDBContext>();

        var liveCount = await dbVerify.OutboxMessages.CountAsync(m => m.Id == row.Id);
        liveCount.Should().Be(0);

        // The row is in the dead table with the expected reason.
        var deadRow = await dbVerify.OutboxDeadMessages
            .FirstOrDefaultAsync(m => m.Id == row.Id);
        deadRow.Should().NotBeNull();
        deadRow!.Reason.Should().Be(Reasons.UnsupportedSchemaVersion);
        deadRow.SchemaVersion.Should().Be(99);
        deadRow.Type.Should().Be(row.Type);
    }
}

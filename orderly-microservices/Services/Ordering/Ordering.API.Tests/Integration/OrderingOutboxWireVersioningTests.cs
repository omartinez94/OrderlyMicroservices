using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using Ordering.Infrastructure.Data.Interceptors;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Wire-format versioning on the bus. The property that has to hold for safe dual-shape rollover
/// is "an old consumer deserializes a new payload" (and vice versa) —
/// additive changes (new optional fields) are non-breaking because
/// <c>System.Text.Json</c> tolerates unknown fields on the read side.
/// This test stages a v1-shaped payload that carries an extra field
/// <c>FutureField</c> the v1 type doesn't declare, runs the dispatcher,
/// and asserts the row was relayed without throwing on the unknown
/// field.
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderingOutboxWireVersioningTests
{
    private readonly OrderingWebApplicationFactory _factory;

    public OrderingOutboxWireVersioningTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NewPayload_ExtraFields_RelayWithoutCrash()
    {
        // Construct a v1-shaped payload with an extra field the v1 type
        // doesn't declare. The dispatcher reads the payload with
        // JsonSerializer.Deserialize(... messageType) and is expected
        // to drop the unknown field silently (System.Text.Json's
        // default behavior).
        var v1PlusExtra =
            $$"""
            {
                "Id": "{{Guid.NewGuid()}}",
                "OccurredOn": "2026-07-10T00:00:00Z",
                "EventType": "{{typeof(VersionedIntegrationEvent).AssemblyQualifiedName}}",
                "MessageVersion": 1,
                "FutureField": "this is from a future v2 publisher"
            }
            """;

        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOn = SystemClock.Instance.GetCurrentInstant(),
            Type = typeof(VersionedIntegrationEvent).AssemblyQualifiedName!,
            Payload = v1PlusExtra,
            SchemaVersion = 1,
        };

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            await db.OutboxMessages.ExecuteDeleteAsync();
            db.OutboxMessages.Add(row);
            await db.SaveChangesAsync();
        }

        var options = Options.Create(new OutboxOptions
        {
            Enabled = true,
            BatchSize = 100,
            MaxSupportedVersion = 1,
        });
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var dispatcher = new OrderingOutboxDispatcher(
            _factory.Services, options, loggerFactory.CreateLogger<OrderingOutboxDispatcher>());

        var dispatched = await dispatcher.DispatchOnceAsync(CancellationToken.None);

        // The row was relayed to the broker — extra field was ignored.
        dispatched.Should().Be(1);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var dbVerify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
        var stamped = await dbVerify.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == row.Id);
        stamped.Should().NotBeNull();
        stamped!.DispatchedAt.Should().NotBeNull();
    }

    [Fact]
    public void MessageVersionDefaults_ToOne()
    {
        // The wire-format protocol relies on MessageVersion = 1 as the
        // default for every existing event. New publishers either keep
        // the default or override to 2+ for a breaking change.
        var fresh = new VersionedIntegrationEvent("hello");
        fresh.MessageVersion.Should().Be(1);
    }
}

/// <summary>
/// Carrier event for the wire-format-versioning tests. The actual
/// shape doesn't matter — what matters is that the JSON payload can
/// carry extra fields the CLR type doesn't know about.
/// </summary>
public record VersionedIntegrationEvent(string Payload) : IntegrationEvent;

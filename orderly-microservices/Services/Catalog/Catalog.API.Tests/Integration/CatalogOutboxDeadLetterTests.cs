using Catalog.API.Infrastructure.Interceptors;

namespace Catalog.API.Tests.Integration;

/// <summary>
/// The Catalog outbox dispatcher routes rows whose
/// <see cref="OutboxMessage.SchemaVersion"/> exceeds
/// <see cref="OutboxOptions.MaxSupportedVersion"/> to the
/// <c>outbox_messages_dead</c> poison table instead of publishing them.
/// This stages one such row, runs a single dispatcher iteration, and
/// asserts the row was moved (not copied) with the expected reason. Mirrors
/// <c>OrderingOutboxDeadLetterTests</c> for the Catalog Postgres dialect.
/// </summary>
[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class CatalogOutboxDeadLetterTests(CatalogWebApplicationFactory factory)
{
    [Fact]
    public async Task FutureVersionRow_IsMovedToDeadTable()
    {
        // Arrange — the outbox tables are shared across the collection, so clear
        // them first: this test asserts on the dispatched count, which must reflect
        // only the row staged below (other tests stage valid v1 rows that would
        // otherwise be relayed and inflate the count).
        await using (var cleanScope = factory.Services.CreateAsyncScope())
        {
            var cleanDb = cleanScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await cleanDb.OutboxMessages.ExecuteDeleteAsync();
            await cleanDb.OutboxDeadMessages.ExecuteDeleteAsync();
        }

        // Stage one outbox row stamped with a future schema version that the
        // dispatcher explicitly cannot route.
        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOn = NodaTime.SystemClock.Instance.GetCurrentInstant(),
            Type = typeof(OrderCompletedIntegrationEvent).FullName!,
            Payload = "{\"OrderId\":\"" + Guid.NewGuid() + "\"}",
            SchemaVersion = 99,
        };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            db.OutboxMessages.Add(row);
            await db.SaveChangesAsync();
        }

        // MaxSupportedVersion = 1 (< the row's 99) so the version gate fires.
        var options = Options.Create(new OutboxOptions
        {
            Enabled = true,
            BatchSize = 100,
            MaxSupportedVersion = 1,
        });
        var dispatcher = new CatalogOutboxDispatcher(
            factory.Services, options, NullLogger<CatalogOutboxDispatcher>.Instance);

        // Act — one dispatcher iteration. The return value is the number of rows
        // *claimed* (DispatchBatchAsync returns pending.Count); the v99 row is
        // claimed and quarantined rather than published, so the substantive
        // assertions below are on the dead-table routing, not the count.
        var dispatched = await dispatcher.DispatchOnceAsync(CancellationToken.None);
        dispatched.Should().Be(1, "the single staged row was claimed and quarantined");

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // The row is gone from the live table.
        var liveCount = await verifyDb.OutboxMessages.CountAsync(m => m.Id == row.Id);
        liveCount.Should().Be(0);

        // The row is in the dead table with the expected reason + version.
        var deadRow = await verifyDb.OutboxDeadMessages.FirstOrDefaultAsync(m => m.Id == row.Id);
        deadRow.Should().NotBeNull();
        deadRow!.Reason.Should().Be(Reasons.UnsupportedSchemaVersion);
        deadRow.SchemaVersion.Should().Be(99);
        deadRow.Type.Should().Be(row.Type);
    }
}

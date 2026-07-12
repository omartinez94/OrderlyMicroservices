namespace Catalog.API.Tests.Integration;

/// <summary>
/// Verifies the Catalog outbox publisher stages an integration event as a
/// live <c>outbox_messages</c> row in the same EF Core transaction as the
/// ambient mutation, stamping <c>SchemaVersion = 1</c> and the event's
/// assembly type name. This is the write half of the Phase 2 outbox path
/// (the dispatcher/dead-letter test covers the relay half).
/// </summary>
[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class CatalogOutboxPublisherTests(CatalogWebApplicationFactory factory)
{
    [Fact]
    public async Task PublishAsync_StagesLiveOutboxRow()
    {
        // Arrange
        var reservationId = Guid.NewGuid();
        var evt = new ReservationReminderDueIntegrationEvent
        {
            ReservationId = reservationId,
            RestaurantId = Guid.NewGuid(),
            ReservationNumber = "RSV-001",
            CustomerName = "Ada",
            ReservationDate = new LocalDate(2026, 7, 12),
            ReservationTime = new LocalTime(19, 0),
            PartySize = 4,
        };

        // Act — publish via the ambient scope's publisher, then SaveChanges (the
        // publisher only stages; the caller's SaveChanges persists the row).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            await publisher.PublishAsync(evt);
            await db.SaveChangesAsync();
        }

        // Assert
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var expectedType = typeof(ReservationReminderDueIntegrationEvent).FullName!;
        var staged = await verifyDb.OutboxMessages
            .Where(m => m.Type == expectedType && m.DispatchedAt == null)
            .ToListAsync();

        staged.Should().ContainSingle(m => m.Payload.Contains(reservationId.ToString()));
        var row = staged.Single(m => m.Payload.Contains(reservationId.ToString()));
        row.SchemaVersion.Should().Be(1);
    }
}

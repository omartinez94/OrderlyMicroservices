namespace Catalog.API.Tests.Integration;

/// <summary>
/// Drives <see cref="ReservationReminderJob.RunAsync"/> against a real
/// Postgres instance with a fixed clock. Confirms a reservation inside the
/// reminder window has <c>ReminderSent</c> flipped and a
/// <see cref="ReservationReminderDueIntegrationEvent"/> staged in the outbox,
/// while a reservation outside the window is left untouched.
/// </summary>
[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class ReservationReminderJobTests(CatalogWebApplicationFactory factory)
{
    [Fact]
    public async Task InsideReminderWindow_StagesReminderAndStamps()
    {
        // Arrange — now = 12:00 UTC. Job window is [now-55m, now+5m) on the reservation instant.
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var restaurantId = Guid.NewGuid();

        var dueId = await SeedReservationAsync(r =>
        {
            r.RestaurantId = restaurantId;
            r.Status = ReservationStatus.Confirmed;
            r.ReminderSent = false;
            r.SeatedAt = null;
            r.ReservationDate = new LocalDate(2026, 7, 12);
            r.ReservationTime = new LocalTime(11, 30); // 11:30 ∈ [11:05, 12:05)
        });

        var farAwayId = await SeedReservationAsync(r =>
        {
            r.RestaurantId = restaurantId;
            r.Status = ReservationStatus.Confirmed;
            r.ReminderSent = false;
            r.SeatedAt = null;
            r.ReservationDate = new LocalDate(2026, 7, 12);
            r.ReservationTime = new LocalTime(18, 0); // outside the window
        });

        // Act
        await RunJobAsync(now);

        // Assert
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var due = await db.Reservations.SingleAsync(r => r.Id == dueId);
        due.ReminderSent.Should().BeTrue();
        due.ReminderSentAt.Should().NotBeNull();

        var farAway = await db.Reservations.SingleAsync(r => r.Id == farAwayId);
        farAway.ReminderSent.Should().BeFalse();

        // The reminder event was staged in the outbox for the due reservation.
        var expectedType = typeof(ReservationReminderDueIntegrationEvent).FullName!;
        var staged = await db.OutboxMessages
            .Where(m => m.Type == expectedType)
            .ToListAsync();
        staged.Should().Contain(m => m.Payload.Contains(dueId.ToString()));
    }

    private async Task<Guid> SeedReservationAsync(Action<Reservation> configure)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var reservation = new Reservation
        {
            ReservationNumber = $"RSV-{Guid.NewGuid():N}".Substring(0, 12),
            CustomerName = "Grace Hopper",
            CustomerPhone = "0000000000",
            CustomerEmail = "grace@test.local",
            PartySize = 2,
        };
        configure(reservation);
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        return reservation.Id;
    }

    private async Task RunJobAsync(DateTimeOffset now)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var fmScope = factory.Services.CreateScope();
        var featureManager = fmScope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var job = new ReservationReminderJob(
            scopeFactory,
            featureManager,
            new TestTimeProvider(now),
            Options.Create(new HangfireOptions()),
            NullLogger<ReservationReminderJob>.Instance);
        await job.RunAsync(CancellationToken.None);
    }
}

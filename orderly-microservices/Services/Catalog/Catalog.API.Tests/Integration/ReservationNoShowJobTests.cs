namespace Catalog.API.Tests.Integration;

/// <summary>
/// Drives <see cref="ReservationNoShowJob.RunAsync"/> against a real
/// Postgres instance with a fixed clock. Confirms a confirmed-but-not-seated
/// reservation past the 15-minute grace window transitions to
/// <see cref="ReservationStatus.NoShow"/>, while a reservation still inside
/// the window is left untouched. This is the interceptor-dependent path the
/// in-memory provider could not exercise (Phase 5 deferred it for that reason).
/// </summary>
[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class ReservationNoShowJobTests(CatalogWebApplicationFactory factory)
{
    [Fact]
    public async Task PastGraceWindow_TransitionsToNoShow()
    {
        // Arrange — now = 12:00 UTC. Grace window is 15 minutes.
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var restaurantId = Guid.NewGuid();

        var lateId = await SeedReservationAsync(r =>
        {
            r.RestaurantId = restaurantId;
            r.Status = ReservationStatus.Confirmed;
            r.SeatedAt = null;
            r.ReservationDate = new LocalDate(2026, 7, 12);
            r.ReservationTime = new LocalTime(11, 0); // 60 min ago → past grace
        });

        var withinWindowId = await SeedReservationAsync(r =>
        {
            r.RestaurantId = restaurantId;
            r.Status = ReservationStatus.Confirmed;
            r.SeatedAt = null;
            r.ReservationDate = new LocalDate(2026, 7, 12);
            r.ReservationTime = new LocalTime(12, 30); // 30 min in the future → keep
        });

        // Act
        await RunJobAsync(now);

        // Assert
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var late = await db.Reservations.SingleAsync(r => r.Id == lateId);
        late.Status.Should().Be(ReservationStatus.NoShow);
        late.CancelledAt.Should().NotBeNull();

        var within = await db.Reservations.SingleAsync(r => r.Id == withinWindowId);
        within.Status.Should().Be(ReservationStatus.Confirmed);
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
        var job = new ReservationNoShowJob(
            scopeFactory,
            featureManager,
            new TestTimeProvider(now),
            Options.Create(new HangfireOptions()),
            NullLogger<ReservationNoShowJob>.Instance);
        await job.RunAsync(CancellationToken.None);
    }
}

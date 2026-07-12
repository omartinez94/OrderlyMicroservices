namespace Catalog.API.Tests.Integration;

/// <summary>
/// Drives <see cref="WalkInNoShowJob.RunAsync"/> against a real Postgres
/// instance with a fixed clock. Confirms a notified walk-in party past the
/// 10-minute response window transitions to
/// <see cref="WalkInQueueStatus.NoShow"/>, while one still inside the window
/// is left untouched.
/// </summary>
[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class WalkInNoShowJobTests(CatalogWebApplicationFactory factory)
{
    [Fact]
    public async Task PastResponseWindow_TransitionsToNoShow()
    {
        // Arrange — now = 12:00 UTC. Response window is 10 minutes → threshold 11:50.
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var restaurantId = Guid.NewGuid();

        var expiredId = await SeedWalkInAsync(w =>
        {
            w.RestaurantId = restaurantId;
            w.Status = WalkInQueueStatus.Notified;
            w.SeatedAt = null;
            w.NotifiedAt = Instant.FromUtc(2026, 7, 12, 11, 45); // 15 min ago → expired
        });

        var freshId = await SeedWalkInAsync(w =>
        {
            w.RestaurantId = restaurantId;
            w.Status = WalkInQueueStatus.Notified;
            w.SeatedAt = null;
            w.NotifiedAt = Instant.FromUtc(2026, 7, 12, 11, 55); // 5 min ago → within window
        });

        // Act
        await RunJobAsync(now);

        // Assert
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var expired = await db.WalkInQueues.SingleAsync(w => w.Id == expiredId);
        expired.Status.Should().Be(WalkInQueueStatus.NoShow);

        var fresh = await db.WalkInQueues.SingleAsync(w => w.Id == freshId);
        fresh.Status.Should().Be(WalkInQueueStatus.Notified);
    }

    private async Task<int> SeedWalkInAsync(Action<WalkInQueue> configure)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var walkIn = new WalkInQueue
        {
            CustomerName = "Walk In",
            CustomerPhone = "0000000000",
            PartySize = 3,
            EstimatedWaitMinutes = 10,
        };
        configure(walkIn);
        db.WalkInQueues.Add(walkIn);
        await db.SaveChangesAsync();
        return walkIn.Id;
    }

    private async Task RunJobAsync(DateTimeOffset now)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var fmScope = factory.Services.CreateScope();
        var featureManager = fmScope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var job = new WalkInNoShowJob(
            scopeFactory,
            featureManager,
            new TestTimeProvider(now),
            Options.Create(new HangfireOptions()),
            NullLogger<WalkInNoShowJob>.Instance);
        await job.RunAsync(CancellationToken.None);
    }
}

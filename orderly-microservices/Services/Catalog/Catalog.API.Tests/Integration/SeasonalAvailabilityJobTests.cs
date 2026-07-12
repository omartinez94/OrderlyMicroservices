namespace Catalog.API.Tests.Integration;

/// <summary>
/// Drives <see cref="SeasonalAvailabilityJob.RunAsync"/> against a real
/// Postgres instance with a fixed clock. Confirms a seasonal item whose
/// window is currently open is flipped to available, one whose window is in
/// the future is flipped to unavailable, and a non-seasonal item is left
/// untouched.
/// </summary>
[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class SeasonalAvailabilityJobTests(CatalogWebApplicationFactory factory)
{
    [Fact]
    public async Task FlipsAvailabilityToMatchSeasonalWindow()
    {
        // Arrange — now = 2026-07-12.
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var restaurantId = await factory.SeedRestaurantAsync();

        // In-season but marked unavailable → should flip to available.
        var inSeasonId = await factory.SeedMenuItemAsync(restaurantId, m =>
        {
            m.ItemType = ItemType.Seasonal;
            m.IsAvailable = false;
            m.SeasonStartDate = new LocalDate(2026, 1, 1);
            m.SeasonEndDate = new LocalDate(2026, 12, 31);
        });

        // Out-of-season (future window) but marked available → should flip to unavailable.
        var futureSeasonId = await factory.SeedMenuItemAsync(restaurantId, m =>
        {
            m.ItemType = ItemType.Seasonal;
            m.IsAvailable = true;
            m.SeasonStartDate = new LocalDate(2026, 8, 1);
            m.SeasonEndDate = new LocalDate(2026, 9, 1);
        });

        // Non-seasonal control → excluded by the job's query, stays as-is.
        var regularId = await factory.SeedMenuItemAsync(restaurantId, m =>
        {
            m.ItemType = ItemType.Regular;
            m.IsAvailable = false;
        });

        // Act
        await RunJobAsync(now);

        // Assert
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        (await db.MenuItems.SingleAsync(m => m.Id == inSeasonId)).IsAvailable.Should().BeTrue();
        (await db.MenuItems.SingleAsync(m => m.Id == futureSeasonId)).IsAvailable.Should().BeFalse();
        (await db.MenuItems.SingleAsync(m => m.Id == regularId)).IsAvailable.Should().BeFalse();
    }

    private async Task RunJobAsync(DateTimeOffset now)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var fmScope = factory.Services.CreateScope();
        var featureManager = fmScope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var job = new SeasonalAvailabilityJob(
            scopeFactory,
            featureManager,
            new TestTimeProvider(now),
            Options.Create(new HangfireOptions()),
            NullLogger<SeasonalAvailabilityJob>.Instance);
        await job.RunAsync(CancellationToken.None);
    }
}

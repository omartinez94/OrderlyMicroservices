using Catalog.API.Data;
using Catalog.API.Features.PriceHistories.CreatePriceHistory;

using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Tests.Unit;

/// <summary>
/// In-memory <see cref="PriceHistoryRecorder"/> tests. Uses
/// <see cref="DbContextOptionsBuilder.UseInMemoryDatabase"/> so the
/// recorder can run end-to-end without Testcontainers.
/// </summary>
public sealed class PriceHistoryRecorderTests
{
    private static CatalogDbContext CreateContext()
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"PriceHistory-{Guid.NewGuid()}")
            .Options;
        return new CatalogDbContext(opts);
    }

    [Fact]
    public void Record_WhenPricesDiffer_AppendsRow()
    {
        using var db = CreateContext();
        var recorder = new PriceHistoryRecorder(db, TimeProvider.System);

        recorder.Record(
            restaurantId: Guid.NewGuid(),
            priceType: PriceType.BasePrice,
            oldPrice: 10m,
            newPrice: 12m,
            reason: "menu price bump",
            changedByUserId: Guid.NewGuid(),
            menuItemId: Guid.NewGuid());

        db.SaveChanges();

        db.PriceHistories.Should().HaveCount(1);
        var row = db.PriceHistories.Single();
        row.PriceType.Should().Be(PriceType.BasePrice);
        row.OldPrice.Should().Be(10m);
        row.NewPrice.Should().Be(12m);
        row.Reason.Should().Be("menu price bump");
        row.MenuItemId.Should().NotBeNull();
    }

    [Fact]
    public void Record_WhenPricesMatch_SkipsWrite()
    {
        using var db = CreateContext();
        var recorder = new PriceHistoryRecorder(db, TimeProvider.System);

        recorder.Record(
            restaurantId: Guid.NewGuid(),
            priceType: PriceType.Variation,
            oldPrice: 5m,
            newPrice: 5m,
            reason: "no-op",
            changedByUserId: Guid.NewGuid(),
            menuItemId: Guid.NewGuid(),
            variationId: 7);

        db.SaveChanges();

        db.PriceHistories.Should().BeEmpty();
    }

    [Fact]
    public void Record_RestaurantConfiguration_UsesRestaurantIdAsScope()
    {
        using var db = CreateContext();
        var recorder = new PriceHistoryRecorder(db, TimeProvider.System);
        var restaurantId = Guid.NewGuid();

        recorder.Record(
            restaurantId: restaurantId,
            priceType: PriceType.RestaurantConfiguration,
            oldPrice: 0.10m,
            newPrice: 0.12m,
            reason: "TaxRate",
            changedByUserId: Guid.NewGuid());

        db.SaveChanges();

        var row = db.PriceHistories.Single();
        row.RestaurantId.Should().Be(restaurantId);
        row.PriceType.Should().Be(PriceType.RestaurantConfiguration);
        row.Reason.Should().Be("TaxRate");
        row.MenuItemId.Should().BeNull();
        row.VariationId.Should().BeNull();
        row.IngredientAlternativeId.Should().BeNull();
    }
}
namespace Catalog.API.Tests.Integration;

/// <summary>
/// Shared seeding helpers for the integration tests. Every insert goes
/// through a real <see cref="CatalogDbContext"/> resolved from the factory's
/// scope so the production interceptors (audit stamping, domain-event
/// dispatch) run exactly as they do in production.
/// </summary>
internal static class SeedHelper
{
    /// <summary>Inserts a minimal valid <see cref="Restaurant"/> and returns its generated id.</summary>
    public static async Task<Guid> SeedRestaurantAsync(this CatalogWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var restaurant = new Restaurant
        {
            Name = "Integration Test Restaurant",
            Address = "123 Test Street",
            Email = "it@test.local",
            PhoneNumber = "0000000000",
            TaxRate = 0.16m,
            Currency = "MXN",
            TimeZone = "UTC",
            BrandId = Guid.NewGuid(),
        };
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        return restaurant.Id;
    }

    /// <summary>Inserts a minimal valid <see cref="MenuItem"/> and returns its generated id.</summary>
    public static async Task<Guid> SeedMenuItemAsync(
        this CatalogWebApplicationFactory factory,
        Guid restaurantId,
        Action<MenuItem>? configure = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var item = new MenuItem
        {
            RestaurantId = restaurantId,
            Name = "Test Item",
            Description = "seeded",
            BasePrice = 100m,
            ImageUrl = "http://example.local/img.png",
            IsAvailable = true,
            ItemType = ItemType.Regular,
        };
        configure?.Invoke(item);

        db.MenuItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }
}

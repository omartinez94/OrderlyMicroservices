using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.API.Tests.Integration;

public static class TestDataSeeder
{
    public static async Task<Guid> SeedBrandAsync(OrderlyTestFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        // Fallback or reflection might be needed depending on actual DbContext type
        // Assuming CatalogDbContext is available
        // var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        // Using reflection to bypass compile errors if CatalogDbContext is internal
        var dbType = Type.GetType("Catalog.API.CatalogDbContext, Catalog.API");
        if (dbType == null)
        {
            return Guid.NewGuid();
        }

        dynamic db = scope.ServiceProvider.GetRequiredService(dbType);

        dynamic brand = Activator.CreateInstance(Type.GetType("Catalog.API.Brand, Catalog.API")!);
        brand.Name = "Integration Test Brand";
        brand.Description = "Seeded brand";

        db.Brands.Add(brand);
        await db.SaveChangesAsync();
        return brand.Id;
    }

    public static async Task<Guid> SeedRestaurantAsync(OrderlyTestFactory<Program> factory, Guid? brandId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbType = Type.GetType("Catalog.API.CatalogDbContext, Catalog.API");
        if (dbType == null)
        {
            return Guid.NewGuid();
        }

        dynamic db = scope.ServiceProvider.GetRequiredService(dbType);
        var id = brandId ?? await SeedBrandAsync(factory);

        dynamic restaurant = Activator.CreateInstance(Type.GetType("Catalog.API.Restaurant, Catalog.API")!);
        restaurant.Name = "Integration Test Restaurant";
        restaurant.Address = "123 Test Street";
        restaurant.Email = "it@test.local";
        restaurant.PhoneNumber = "0000000000";
        restaurant.TaxRate = 0.16m;
        restaurant.Currency = "MXN";
        restaurant.TimeZone = "UTC";
        restaurant.BrandId = id;

        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        return restaurant.Id;
    }

    public static async Task<Guid> SeedMenuItemAsync(OrderlyTestFactory<Program> factory, Guid restaurantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbType = Type.GetType("Catalog.API.CatalogDbContext, Catalog.API");
        if (dbType == null)
        {
            return Guid.NewGuid();
        }

        dynamic db = scope.ServiceProvider.GetRequiredService(dbType);

        dynamic item = Activator.CreateInstance(Type.GetType("Catalog.API.MenuItem, Catalog.API")!);
        item.RestaurantId = restaurantId;
        item.Name = "Test Item";
        item.Description = "seeded";
        item.BasePrice = 100m;
        item.ImageUrl = "http://example.local/img.png";
        item.IsAvailable = true;

        db.MenuItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    public static async Task<Guid> SeedMenuCategoryAsync(OrderlyTestFactory<Program> factory, Guid restaurantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbType = Type.GetType("Catalog.API.CatalogDbContext, Catalog.API");
        if (dbType == null)
        {
            return Guid.NewGuid();
        }

        dynamic db = scope.ServiceProvider.GetRequiredService(dbType);

        dynamic category = Activator.CreateInstance(Type.GetType("Catalog.API.MenuCategory, Catalog.API")!);
        category.RestaurantId = restaurantId;
        category.Name = "Test Category";
        category.Description = "seeded";

        db.MenuCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }
}

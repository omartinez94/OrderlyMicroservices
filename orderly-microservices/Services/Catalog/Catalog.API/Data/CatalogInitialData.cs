using Marten.Schema;

namespace Catalog.API.Data;

public class CatalogInitialData : IInitialData
{
    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        using var session = store.LightweightSession();

        // Brands
        var brandCount = await session.Query<Brand>().CountAsync(cancellation);
        if (brandCount == 0)
        {
            session.Store(GetPreconfiguredBrands());
        }

        // Restaurants
        var restaurantCount = await session.Query<Restaurant>().CountAsync(cancellation);
        if (restaurantCount == 0)
        { 
            session.Store(GetPreconfiguredRestaurants());
        }

        await session.SaveChangesAsync();
    }

    private static IEnumerable<Brand> GetPreconfiguredBrands()
    {
        return
        [
            new Brand
            {
                Id = new("5334c996-8457-4cf0-815c-ed2b77c4ef61"),
                Name = "Kalaa",
                Description = "Kalaa Authentic Cuisine",
                ContactEmail = "contact@kalaa.com",
                ContactPhone = "555-0100",
                IsActive = true
            },
            new Brand
            {
                Id = new("1111c996-8457-4cf0-815c-ed2b77c4ef11"),
                Name = "BurgerHub",
                Description = "Premium Burgers",
                ContactEmail = "contact@burgerhub.com",
                ContactPhone = "555-0200",
                IsActive = true
            },
            new Brand
            {
                Id = new("2222c996-8457-4cf0-815c-ed2b77c4ef22"),
                Name = "SushiWay",
                Description = "Fresh Japanese Cuisine",
                ContactEmail = "info@sushiway.com",
                ContactPhone = "555-0300",
                IsActive = true
            }
        ];
    }

    private static IEnumerable<Restaurant> GetPreconfiguredRestaurants()
    {
        List<Brand> brands = [.. GetPreconfiguredBrands()];

        return
        [
            new Restaurant
            {
                Id = Guid.NewGuid(),
                BrandId = brands[0].Id,
                Name = "Kalaa",
                Address = "Downtown St 123",
                PhoneNumber = "555-0101",
                Email = "centro@kalaa.com",
                TaxRate = 0.16m,
                Currency = "MXN",
                TimeZone = "America/Mexico_City"
            },
            new Restaurant
            {
                Id = Guid.NewGuid(),
                BrandId = brands[1].Id,
                Name = "BurgerHub North",
                Address = "North Ave 45",
                PhoneNumber = "555-0201",
                Email = "north@burgerhub.com",
                TaxRate = 0.1m,
                Currency = "USD",
                TimeZone = "America/New_York"
            },
            new Restaurant
            {
                Id = Guid.NewGuid(),
                BrandId = brands[1].Id,
                Name = "BurgerHub South",
                Address = "South Blvd 89",
                PhoneNumber = "555-0202",
                Email = "south@burgerhub.com",
                TaxRate = 0.1m,
                Currency = "USD",
                TimeZone = "America/New_York"
            },
            new Restaurant
            {
                Id = Guid.NewGuid(),
                BrandId = brands[2].Id,
                Name = "SushiWay East",
                Address = "East Plaza 12",
                PhoneNumber = "555-0301",
                Email = "east@sushiway.com",
                TaxRate = 0.08m,
                Currency = "USD",
                TimeZone = "America/Los_Angeles"
            },
            new Restaurant
            {
                Id = Guid.NewGuid(),
                BrandId = brands[2].Id,
                Name = "SushiWay West",
                Address = "West Mall 34",
                PhoneNumber = "555-0302",
                Email = "west@sushiway.com",
                TaxRate = 0.08m,
                Currency = "USD",
                TimeZone = "America/Los_Angeles"
            }
        ];
    }
}

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
            await session.SaveChangesAsync();
        }
        
        // NOTE: Restaurants are now managed via EF Core migrations/seed
        // Restaurant seeding was removed - they are relational entities in CatalogDbContext
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
}

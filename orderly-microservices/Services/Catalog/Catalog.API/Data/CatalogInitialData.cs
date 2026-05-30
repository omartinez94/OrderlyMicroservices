using Marten.Schema;

namespace Catalog.API.Data;

public class CatalogInitialData : IInitialData
{
    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        using var session = store.LightweightSession();

        // NOTE: Brands and Restaurants are now managed via EF Core migrations/seed
        // All entity seeding was removed - they are relational entities in CatalogDbContext
    }
}

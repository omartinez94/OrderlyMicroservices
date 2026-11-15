using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Data;

namespace Ordering.API.Tests.Integration;

public class TestCatalogPricingClient(IServiceProvider serviceProvider) : ICatalogPricingClient
{
    public static Func<Guid, CancellationToken, Task<CatalogMenuSnapshot>>? CustomMenuSnapshotHandler { get; set; }
    public static Func<Guid, Guid, CancellationToken, Task<CatalogTableDto?>>? CustomTableHandler { get; set; }

    public static void Reset()
    {
        CustomMenuSnapshotHandler = null;
        CustomTableHandler = null;
    }

    public async Task<CatalogMenuSnapshot> GetMenuSnapshotAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        if (CustomMenuSnapshotHandler is not null)
        {
            return await CustomMenuSnapshotHandler(restaurantId, cancellationToken);
        }

        using IServiceScope scope = serviceProvider.CreateScope();
        IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var menuItems = await db.MenuItems.AsNoTracking().ToListAsync(cancellationToken);

        List<CatalogMenuItemDto> items = menuItems
            .Select(m => new CatalogMenuItemDto(
                m.Id.Value,
                m.Name,
                m.Price,
                null,
                true,
                "Available",
                []))
            .ToList();

        return new CatalogMenuSnapshot(restaurantId, items);
    }

    public Task<CatalogTableDto?> GetTableAsync(Guid restaurantId, Guid tableId, CancellationToken cancellationToken = default)
    {
        if (CustomTableHandler is not null)
        {
            return CustomTableHandler(restaurantId, tableId, cancellationToken);
        }

        return Task.FromResult<CatalogTableDto?>(new CatalogTableDto(tableId, restaurantId, "T1", 20, "Available"));
    }
}

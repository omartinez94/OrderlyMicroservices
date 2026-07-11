using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Readers;

/// <summary>
/// Concrete <see cref="IMenuReader"/> that assembles the menu tree directly
/// from <see cref="CatalogDbContext"/>. Wrapped by <see cref="Caching.CachedMenuReader"/>
/// at the DI container via Scrutor; never resolves to this type directly from
/// application code.
/// </summary>
/// <remarks>
/// <para>Loads in four small round-trips (categories, sub-categories, items,
/// variations-and-ingredients) rather than a single fat <c>.Include</c> chain
/// because <c>MenuItem</c> has no navigation properties configured in
/// <c>CatalogDbContext.OnModelCreating</c> — joins are explicit.</para>
/// <para>Global soft-delete query filters on <c>MenuCategory</c>,
/// <c>MenuSubCategory</c>, <c>MenuItem</c>, and <c>MenuItemVariation</c>
/// (<c>CatalogDbContext.OnModelCreating</c>) filter the deleted rows
/// automatically — no explicit <c>!IsDeleted</c> predicates needed here.</para>
/// </remarks>
public sealed class MenuReader(CatalogDbContext dbContext) : IMenuReader
{
    /// <inheritdoc/>
    public async Task<MenuSnapshot?> GetByRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var categories = await dbContext.MenuCategories
            .AsNoTracking()
            .Where(c => c.RestaurantId == restaurantId)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (categories.Count == 0)
        {
            return null;
        }

        var categoryIds = categories.Select(c => c.Id).ToList();

        var subCategories = await dbContext.MenuSubCategories
            .AsNoTracking()
            .Where(msc => categoryIds.Contains(msc.CategoryId))
            .OrderBy(msc => msc.DisplayOrder)
            .ThenBy(msc => msc.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var subCategoryIds = subCategories.Select(msc => msc.Id).ToList();

        var items = subCategoryIds.Count == 0
            ? new List<Models.MenuItem>()
            : await dbContext.MenuItems
                .AsNoTracking()
                .Where(mi => mi.SubCategoryId != null && subCategoryIds.Contains(mi.SubCategoryId.Value))
                .OrderBy(mi => mi.DisplayOrder)
                .ThenBy(mi => mi.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var itemIds = items.Select(i => i.Id).ToList();

        // Variations and ingredients are joined in via separate queries because
        // MenuItem has no navigation properties configured in the DbContext.
        var variations = itemIds.Count == 0
            ? new List<Models.MenuItemVariation>()
            : await dbContext.MenuItemVariations
                .AsNoTracking()
                .Where(v => itemIds.Contains(v.MenuItemId))
                .OrderBy(v => v.DisplayOrder)
                .ThenBy(v => v.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var ingredientLinks = itemIds.Count == 0
            ? new List<Models.MenuItemIngredient>()
            : await dbContext.MenuItemIngredients
                .AsNoTracking()
                .Where(link => itemIds.Contains(link.MenuItemId))
                .OrderBy(link => link.IngredientId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var categoryNodes = categories.Select(c => new MenuCategoryNode(
            Id: c.Id,
            Name: c.Name,
            Description: c.Description,
            DisplayOrder: c.DisplayOrder,
            SubCategories: BuildSubCategoryNodes(c.Id, subCategories, items, variations, ingredientLinks))).ToList();

        return new MenuSnapshot(
            RestaurantId: restaurantId,
            SnapshotAt: SystemClock.Instance.GetCurrentInstant(),
            Categories: categoryNodes);
    }

    private static IReadOnlyList<MenuSubCategoryNode> BuildSubCategoryNodes(
        int categoryId,
        IReadOnlyList<Models.MenuSubCategory> subCategories,
        IReadOnlyList<Models.MenuItem> items,
        IReadOnlyList<Models.MenuItemVariation> variations,
        IReadOnlyList<Models.MenuItemIngredient> ingredientLinks)
    {
        return subCategories
            .Where(msc => msc.CategoryId == categoryId)
            .Select(msc => new MenuSubCategoryNode(
                Id: msc.Id,
                Name: msc.Name,
                Description: msc.Description,
                DisplayOrder: msc.DisplayOrder,
                IsActive: msc.IsActive,
                Items: BuildItemNodes(msc.Id, items, variations, ingredientLinks)))
            .ToList();
    }

    private static IReadOnlyList<MenuItemNode> BuildItemNodes(
        int subCategoryId,
        IReadOnlyList<Models.MenuItem> items,
        IReadOnlyList<Models.MenuItemVariation> variations,
        IReadOnlyList<Models.MenuItemIngredient> ingredientLinks)
    {
        return items
            .Where(mi => mi.SubCategoryId == subCategoryId)
            .Select(mi => new MenuItemNode(
                Id: mi.Id,
                Name: mi.Name,
                Description: mi.Description,
                BasePrice: mi.BasePrice,
                PromoPrice: mi.PromoPrice,
                IsAvailable: mi.IsAvailable,
                AvailabilityStatus: mi.AvailabilityStatus.ToString(),
                ItemType: mi.ItemType.ToString(),
                DisplayOrder: mi.DisplayOrder,
                PrepTimeMinutes: mi.PrepTimeMinutes,
                PrepTimeMaxMinutes: mi.PrepTimeMaxMinutes,
                ImageUrl: mi.ImageUrl,
                Variations: BuildVariationNodes(mi.Id, variations),
                Ingredients: BuildIngredientNodes(mi.Id, ingredientLinks)))
            .ToList();
    }

    private static IReadOnlyList<MenuItemVariationNode> BuildVariationNodes(
        Guid menuItemId,
        IReadOnlyList<Models.MenuItemVariation> variations)
    {
        return variations
            .Where(v => v.MenuItemId == menuItemId)
            .Select(v => new MenuItemVariationNode(
                Id: v.Id,
                Name: v.Name,
                VariationValue: v.VariationValue,
                PriceModifier: v.PriceModifier,
                IsDefault: v.IsDefault,
                DisplayOrder: v.DisplayOrder))
            .ToList();
    }

    private static IReadOnlyList<MenuItemIngredientNode> BuildIngredientNodes(
        Guid menuItemId,
        IReadOnlyList<Models.MenuItemIngredient> ingredientLinks)
    {
        return ingredientLinks
            .Where(link => link.MenuItemId == menuItemId)
            .Select(link => new MenuItemIngredientNode(
                Id: link.Id,
                IngredientId: link.IngredientId,
                QuantityRequired: link.QuantityRequired,
                IsOptional: link.IsOptional))
            .ToList();
    }
}
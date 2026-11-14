using System.Text.Json;
using BuildingBlocks.Entities.Interfaces;
using BuildingBlocks.Identifiers;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Catalog.API.Data;

public class CatalogDataSeederHostedService(
    IServiceProvider serviceProvider,
    IWebHostEnvironment environment,
    ILogger<CatalogDataSeederHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            CatalogDbContext dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await CatalogDataSeeder.SeedDataAsync(dbContext, environment, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Catalog data");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class CatalogDataSeeder
{
    private static readonly Instant SeedTimestamp = Instant.FromUtc(2026, 1, 1, 0, 0, 0);

    public static readonly Guid AcmeBrandId = Guid.Parse("a4f1c8e2-6b3d-4e9a-9f5c-8d1e2a3b4c5d");
    public static readonly Guid KalaaBrandId = Guid.Parse("f2c9e1b4-7a5d-4c8e-8b3f-1d4e5a6b7c8d");

    public static readonly Guid AcmeRomaNorteId = Guid.Parse("e8d2a1b5-9c4f-4e7a-8f1d-2b3c4d5e6a7b");
    public static readonly Guid AcmeSanPedroId = Guid.Parse("c5b1e9a4-3f7d-4a8e-9c2b-6d7e8f1a2b3c");
    public static readonly Guid KalaaPolancoId = Guid.Parse("d1a7f3e5-8c2b-4e9d-9a4f-5b6c7d8e9f0a");

    public static async Task SeedDataAsync(
        CatalogDbContext dbContext,
        IWebHostEnvironment environment,
        ILogger logger,
        CancellationToken ct = default)
    {
        await SeedBrandsAndRestaurantsAsync(dbContext, ct);
        await SeedTablesAsync(dbContext, ct);
        await SeedMexicanMenuAsync(dbContext, environment, logger, ct);
    }

    public static async Task SeedBrandsAndRestaurantsAsync(CatalogDbContext dbContext, CancellationToken ct)
    {
        if (!await dbContext.Brands.AnyAsync(b => b.Id == AcmeBrandId, ct))
        {
            Brand acmeBrand = new Brand
            {
                Id = AcmeBrandId,
                ShortId = "brdm7k8x9pq",
                Name = "Acme",
                Description = "Cantina tradicional y cocina mexicana contemporánea.",
                LogoUrl = "https://placehold.co/100x100/png?text=Acme",
                ContactEmail = "contacto@acme.mx",
                ContactPhone = "+52 55 5584 9231",
                WebsiteUrl = "https://acme.mx",
                CuisineType = BuildingBlocks.Enums.CuisineType.Mexican
            };
            ((IAuditableEntity)acmeBrand).CreatedFrom("system", SeedTimestamp);
            dbContext.Brands.Add(acmeBrand);
        }

        if (!await dbContext.Brands.AnyAsync(b => b.Id == KalaaBrandId, ct))
        {
            Brand kalaaBrand = new Brand
            {
                Id = KalaaBrandId,
                ShortId = "brdp3w4e6tr",
                Name = "Kalaa",
                Description = "Alta gastronomía mexicana de raíz con técnicas ancestrales.",
                LogoUrl = "https://placehold.co/100x100/png?text=Kalaa",
                ContactEmail = "contacto@kalaa.mx",
                ContactPhone = "+52 55 5280 4421",
                WebsiteUrl = "https://kalaa.mx",
                CuisineType = BuildingBlocks.Enums.CuisineType.Mexican
            };
            ((IAuditableEntity)kalaaBrand).CreatedFrom("system", SeedTimestamp);
            dbContext.Brands.Add(kalaaBrand);
        }

        if (!await dbContext.Restaurants.AnyAsync(r => r.Id == AcmeRomaNorteId, ct))
        {
            Restaurant acmeRomaNorte = new Restaurant
            {
                Id = AcmeRomaNorteId,
                ShortId = "rstv8n2k5mx",
                BrandId = AcmeBrandId,
                Name = "Acme Cantina Tradicional - Roma Norte",
                Address = "Colima 168, Roma Norte, Cuauhtémoc, 06700 Ciudad de México, CDMX",
                PhoneNumber = "+52 55 5584 9231",
                Email = "romanorte@acme.mx",
                TaxRate = 0.16m,
                Currency = "MXN",
                TimeZone = "America/Mexico_City"
            };
            ((IAuditableEntity)acmeRomaNorte).CreatedFrom("system", SeedTimestamp);
            dbContext.Restaurants.Add(acmeRomaNorte);
        }

        if (!await dbContext.Restaurants.AnyAsync(r => r.Id == AcmeSanPedroId, ct))
        {
            Restaurant acmeSanPedro = new Restaurant
            {
                Id = AcmeSanPedroId,
                ShortId = "rstc9h4w7tz",
                BrandId = AcmeBrandId,
                Name = "Acme Grill & Bar - San Pedro",
                Address = "Av. Vasconcelos 345, Del Valle, 66220 San Pedro Garza García, N.L.",
                PhoneNumber = "+52 81 8356 7410",
                Email = "sanpedro@acme.mx",
                TaxRate = 0.16m,
                Currency = "MXN",
                TimeZone = "America/Monterrey"
            };
            ((IAuditableEntity)acmeSanPedro).CreatedFrom("system", SeedTimestamp);
            dbContext.Restaurants.Add(acmeSanPedro);
        }

        if (!await dbContext.Restaurants.AnyAsync(r => r.Id == KalaaPolancoId, ct))
        {
            Restaurant kalaaPolanco = new Restaurant
            {
                Id = KalaaPolancoId,
                ShortId = "rstk2m8b4qy",
                BrandId = KalaaBrandId,
                Name = "Kalaa Cocina Raíz - Polanco",
                Address = "Campos Elíseos 204, Polanco, Miguel Hidalgo, 11560 Ciudad de México, CDMX",
                PhoneNumber = "+52 55 5280 4421",
                Email = "polanco@kalaa.mx",
                TaxRate = 0.16m,
                Currency = "MXN",
                TimeZone = "America/Mexico_City"
            };
            ((IAuditableEntity)kalaaPolanco).CreatedFrom("system", SeedTimestamp);
            dbContext.Restaurants.Add(kalaaPolanco);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public static async Task SeedTablesAsync(CatalogDbContext dbContext, CancellationToken ct)
    {
        Guid[] restaurantIds = [AcmeRomaNorteId, AcmeSanPedroId];

        foreach (Guid restaurantId in restaurantIds)
        {
            if (await dbContext.Tables.AnyAsync(t => t.RestaurantId == restaurantId, ct))
            {
                continue;
            }

            var tableConfigs = new[]
            {
                new { Number = "T-01", Capacity = 2 },
                new { Number = "T-02", Capacity = 2 },
                new { Number = "T-03", Capacity = 4 },
                new { Number = "T-04", Capacity = 4 },
                new { Number = "T-05", Capacity = 4 },
                new { Number = "T-06", Capacity = 4 },
                new { Number = "T-07", Capacity = 6 },
                new { Number = "T-08", Capacity = 6 },
                new { Number = "TER-01", Capacity = 4 },
                new { Number = "TER-02", Capacity = 4 },
                new { Number = "TER-03", Capacity = 6 },
                new { Number = "TER-04", Capacity = 10 },
                new { Number = "BAR-01", Capacity = 1 },
                new { Number = "BAR-02", Capacity = 1 },
                new { Number = "BAR-03", Capacity = 2 },
                new { Number = "BAR-04", Capacity = 2 }
            };

            foreach (var cfg in tableConfigs)
            {
                Table table = new Table
                {
                    Id = Guid.NewGuid(),
                    RestaurantId = restaurantId,
                    TableNumber = cfg.Number,
                    Capacity = cfg.Capacity,
                    ShortId = ShortIdGenerator.Generate(EntityPrefix.Table),
                    Status = TableStatus.Available
                };
                ((IAuditableEntity)table).CreatedFrom("system", SeedTimestamp);
                dbContext.Tables.Add(table);
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public static async Task SeedMexicanMenuAsync(
        CatalogDbContext dbContext,
        IWebHostEnvironment environment,
        ILogger logger,
        CancellationToken ct)
    {
        string seedPath = Path.Combine(AppContext.BaseDirectory, "Data", "mexican_menu_seed_data.json");
        if (!File.Exists(seedPath))
        {
            seedPath = Path.Combine(environment.ContentRootPath, "Data", "mexican_menu_seed_data.json");
        }

        if (!File.Exists(seedPath))
        {
            logger.LogWarning("Seed file not found at {SeedPath}. Skipping Mexican menu seed.", seedPath);
            return;
        }

        string jsonContent = await File.ReadAllTextAsync(seedPath, ct);
        using JsonDocument doc = JsonDocument.Parse(jsonContent);
        JsonElement root = doc.RootElement;

        Guid[] targetRestaurants = [AcmeRomaNorteId, AcmeSanPedroId];

        if (!root.TryGetProperty("categories", out JsonElement categoriesElem) ||
            !root.TryGetProperty("items", out JsonElement itemsElem))
        {
            return;
        }

        foreach (Guid restaurantId in targetRestaurants)
        {
            // 1. Seed Categories & SubCategories per restaurant
            Dictionary<int, int> subCategoryIdMap = new();

            foreach (JsonElement catElem in categoriesElem.EnumerateArray())
            {
                string name = catElem.GetProperty("name").GetString() ?? string.Empty;
                string description = catElem.GetProperty("description").GetString() ?? string.Empty;
                int displayOrder = catElem.GetProperty("displayOrder").GetInt32();

                MenuCategory? existingCat = await dbContext.MenuCategories
                    .FirstOrDefaultAsync(c => c.Name == name && c.RestaurantId == restaurantId, ct);

                if (existingCat is null)
                {
                    existingCat = new MenuCategory
                    {
                        Name = name,
                        Description = description,
                        DisplayOrder = displayOrder,
                        RestaurantId = restaurantId
                    };
                    ((IAuditableEntity)existingCat).CreatedFrom("system", SeedTimestamp);
                    dbContext.MenuCategories.Add(existingCat);
                    await dbContext.SaveChangesAsync(ct);
                }

                if (catElem.TryGetProperty("subCategories", out JsonElement subCatsElem))
                {
                    foreach (JsonElement subElem in subCatsElem.EnumerateArray())
                    {
                        int jsonSubId = subElem.GetProperty("id").GetInt32();
                        string subName = subElem.GetProperty("name").GetString() ?? string.Empty;
                        string subDesc = subElem.GetProperty("description").GetString() ?? string.Empty;
                        int subOrder = subElem.GetProperty("displayOrder").GetInt32();

                        MenuSubCategory? existingSub = await dbContext.MenuSubCategories
                            .FirstOrDefaultAsync(s => s.Name == subName && s.CategoryId == existingCat.Id, ct);

                        if (existingSub is null)
                        {
                            existingSub = new MenuSubCategory
                            {
                                CategoryId = existingCat.Id,
                                Name = subName,
                                Description = subDesc,
                                DisplayOrder = subOrder,
                                IsActive = true
                            };
                            dbContext.MenuSubCategories.Add(existingSub);
                            await dbContext.SaveChangesAsync(ct);
                        }

                        subCategoryIdMap[jsonSubId] = existingSub.Id;
                    }
                }
            }

            // 2. Seed Items, Variations, Ingredients for restaurant if not already present
            bool hasItems = await dbContext.MenuItems.AnyAsync(mi => mi.RestaurantId == restaurantId, ct);
            if (hasItems)
            {
                // Ensure subcategory links point to this restaurant's subcategories
                List<MenuItem> existingItems = await dbContext.MenuItems
                    .Where(mi => mi.RestaurantId == restaurantId)
                    .ToListAsync(ct);

                bool itemsUpdated = false;
                foreach (JsonElement itemElem in itemsElem.EnumerateArray())
                {
                    string itemName = itemElem.GetProperty("name").GetString() ?? string.Empty;
                    int jsonSubCatId = itemElem.GetProperty("subCategoryId").GetInt32();
                    if (subCategoryIdMap.TryGetValue(jsonSubCatId, out int correctSubCatId))
                    {
                        MenuItem? item = existingItems.FirstOrDefault(i => i.Name == itemName);
                        if (item is not null && item.SubCategoryId != correctSubCatId)
                        {
                            item.SubCategoryId = correctSubCatId;
                            itemsUpdated = true;
                        }
                    }
                }

                if (itemsUpdated)
                {
                    await dbContext.SaveChangesAsync(ct);
                }

                continue;
            }

            int itemDisplayOrder = 1;
            foreach (JsonElement itemElem in itemsElem.EnumerateArray())
            {
                string name = itemElem.GetProperty("name").GetString() ?? string.Empty;
                string description = itemElem.GetProperty("description").GetString() ?? string.Empty;
                decimal basePrice = itemElem.GetProperty("basePrice").GetDecimal();
                int prepMin = itemElem.GetProperty("prepTimeMinutes").GetInt32();
                int prepMax = itemElem.GetProperty("prepTimeMaxMinutes").GetInt32();
                int course = itemElem.TryGetProperty("defaultCourseNumber", out var cElem) ? cElem.GetInt32() : 1;
                int jsonSubCatId = itemElem.GetProperty("subCategoryId").GetInt32();
                int? mappedSubCatId = subCategoryIdMap.TryGetValue(jsonSubCatId, out int dbSubId) ? dbSubId : null;

                Guid itemId = (restaurantId == AcmeRomaNorteId && itemElem.TryGetProperty("id", out var idElem))
                    ? Guid.Parse(idElem.GetString()!)
                    : Guid.NewGuid();

                string shortId = (restaurantId == AcmeRomaNorteId && itemElem.TryGetProperty("shortId", out var sElem))
                    ? sElem.GetString()!
                    : ShortIdGenerator.Generate(EntityPrefix.MenuItem);

                MenuItem menuItem = new MenuItem
                {
                    Id = itemId,
                    RestaurantId = restaurantId,
                    SubCategoryId = mappedSubCatId,
                    Name = name,
                    Description = description,
                    BasePrice = basePrice,
                    PrepTimeMinutes = prepMin,
                    PrepTimeMaxMinutes = prepMax,
                    DefaultCourseNumber = course,
                    ShortId = shortId,
                    ItemType = ItemType.Regular,
                    AvailabilityStatus = AvailabilityStatus.Available,
                    IsAvailable = true,
                    DisplayOrder = itemDisplayOrder++
                };
                ((IAuditableEntity)menuItem).CreatedFrom("system", SeedTimestamp);
                dbContext.MenuItems.Add(menuItem);

                // Variations
                if (itemElem.TryGetProperty("variations", out JsonElement varsElem))
                {
                    foreach (JsonElement vElem in varsElem.EnumerateArray())
                    {
                        MenuItemVariation variation = new MenuItemVariation
                        {
                            MenuItemId = menuItem.Id,
                            Name = vElem.GetProperty("name").GetString() ?? "Opción",
                            VariationValue = vElem.GetProperty("variationValue").GetString() ?? string.Empty,
                            PriceModifier = vElem.GetProperty("priceModifier").GetDecimal(),
                            IsDefault = vElem.GetProperty("isDefault").GetBoolean(),
                            DisplayOrder = vElem.GetProperty("displayOrder").GetInt32()
                        };
                        dbContext.MenuItemVariations.Add(variation);
                    }
                }

                // Ingredients
                if (itemElem.TryGetProperty("ingredients", out JsonElement ingsElem))
                {
                    foreach (JsonElement ingElem in ingsElem.EnumerateArray())
                    {
                        string ingName = ingElem.GetProperty("name").GetString() ?? string.Empty;
                        string unit = ingElem.GetProperty("unit").GetString() ?? "unidad";
                        decimal qty = ingElem.GetProperty("quantity").GetDecimal();
                        bool isOptional = ingElem.GetProperty("isOptional").GetBoolean();

                        Ingredient? ingredient = await dbContext.Ingredients
                            .FirstOrDefaultAsync(i => i.Name == ingName && i.RestaurantId == restaurantId, ct);

                        if (ingredient is null)
                        {
                            ingredient = new Ingredient
                            {
                                RestaurantId = restaurantId,
                                Name = ingName,
                                Unit = unit,
                                CurrentStock = 100m,
                                MinimumStock = 10m,
                                IsAvailable = true
                            };
                            ((IAuditableEntity)ingredient).CreatedFrom("system", SeedTimestamp);
                            dbContext.Ingredients.Add(ingredient);
                            await dbContext.SaveChangesAsync(ct);
                        }

                        MenuItemIngredient recipeLine = new MenuItemIngredient
                        {
                            MenuItemId = menuItem.Id,
                            IngredientId = ingredient.Id,
                            QuantityRequired = qty,
                            IsOptional = isOptional
                        };
                        dbContext.MenuItemIngredients.Add(recipeLine);
                    }
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation("Successfully seeded 40 authentic Mexican menu items into CatalogDb");
    }
}

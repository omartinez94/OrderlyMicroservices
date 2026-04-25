using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Data;

/// <summary>
/// Database context for the Catalog Service
/// Manages: Restaurants, Tables, Menu, Ingredients
/// </summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    // ═══════════════════════════════════════════════════
    // RESTAURANT & INFRASTRUCTURE
    // ═══════════════════════════════════════════════════
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Table> Tables => Set<Table>();
    
    /// <summary>
    /// Tracks when multiple tables are pushed together to accommodate larger parties.
    /// </summary>
    public DbSet<MergedTable> MergedTables => Set<MergedTable>();
    
    public DbSet<Reservation> Reservations => Set<Reservation>();
    
    /// <summary>
    /// Manages the waitlist queue for walk-in customers waiting for an available table.
    /// </summary>
    public DbSet<WalkInQueue> WalkInQueues => Set<WalkInQueue>();

    // ═══════════════════════════════════════════════════
    // MENU & INGREDIENTS STRUCTURE
    // ═══════════════════════════════════════════════════
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
    public DbSet<MenuSubCategory> MenuSubCategories => Set<MenuSubCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    
    /// <summary>
    /// Tracks variations or sizes for a single menu item (e.g., Small vs Large, Mild vs Spicy).
    /// </summary>
    public DbSet<MenuItemVariation> MenuItemVariations => Set<MenuItemVariation>();
    
    /// <summary>
    /// Defines which individual menu items are bundled together to form a combo meal.
    /// </summary>
    public DbSet<ComboItem> ComboItems => Set<ComboItem>();
    
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<MenuItemIngredient> MenuItemIngredients => Set<MenuItemIngredient>();
    
    /// <summary>
    /// Specifies substitute ingredients that can be used if a primary ingredient is out of stock.
    /// </summary>
    public DbSet<IngredientAlternative> IngredientAlternatives => Set<IngredientAlternative>();

    /// <summary>
    /// Tracks historical price changes for menu items, variations, and ingredient alternatives over time.
    /// </summary>
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();

    // ═══════════════════════════════════════════════════
    // ANALYTICS
    // ═══════════════════════════════════════════════════
    public DbSet<CustomerFeedback> CustomerFeedbacks => Set<CustomerFeedback>();
    
    /// <summary>
    /// Records detailed timestamps for each phase of an order to analyze service speed and prep times.
    /// </summary>
    public DbSet<OrderTimingAnalytics> OrderTimingAnalytics => Set<OrderTimingAnalytics>();
    
    /// <summary>
    /// Aggregates statistics on how often items are ordered, prep times, and revenue generation.
    /// </summary>
    public DbSet<MenuItemAnalytics> MenuItemAnalytics => Set<MenuItemAnalytics>();
    
    /// <summary>
    /// Tracks asynchronous background tasks for uploading and importing bulk orders from files.
    /// </summary>
    public DbSet<BulkOrderUpload> BulkOrderUploads => Set<BulkOrderUpload>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ═══════════════════════════════════════════════════
        // RESTAURANT CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<Restaurant>(entity =>
        {
            entity.ToTable("Restaurants");
            entity.HasKey(r => r.Id);

            entity.Property(r => r.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(r => r.Address)
                .IsRequired();

            entity.Property(r => r.Email)
                .HasMaxLength(255);

            entity.Property(r => r.PhoneNumber)
                .HasMaxLength(20);

            entity.Property(r => r.TaxRate)
                .HasColumnType("decimal(5,2)")
                .IsRequired();

            entity.Property(r => r.Currency)
                .HasMaxLength(3)
                .HasDefaultValue("MXN");

            entity.Property(r => r.TimeZone)
                .HasMaxLength(50)
                .HasDefaultValue("America/Monterrey");

            entity.Property(r => r.IsActive)
                .HasDefaultValue(true);

            entity.Property(r => r.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(r => r.LastModifiedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Indexes
            entity.HasIndex(r => r.BrandId);
            entity.HasIndex(r => r.IsActive);
        });

        // ═══════════════════════════════════════════════════
        // TABLE CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<Table>(entity =>
        {
            entity.ToTable("Tables");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.TableNumber)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(t => t.Capacity)
                .IsRequired();

            entity.Property(t => t.Shape)
                .HasMaxLength(20)
                .HasDefaultValue("rectangle");

            entity.Property(t => t.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("available");

            entity.Property(t => t.IsActive)
                .HasDefaultValue(true);

            entity.Property(t => t.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(t => t.LastModifiedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Relationships
            entity.HasOne<Restaurant>()
                .WithMany()
                .HasForeignKey(t => t.RestaurantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique constraint: TableNumber per Restaurant
            entity.HasIndex(t => new { t.RestaurantId, t.TableNumber })
                .IsUnique();

            // Indexes
            entity.HasIndex(t => t.RestaurantId);
            entity.HasIndex(t => t.Status);
        });

        // ═══════════════════════════════════════════════════
        // MERGED TABLES CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<MergedTable>(entity =>
        {
            entity.ToTable("MergedTables");
            entity.HasKey(mt => mt.Id);

            entity.Property(mt => mt.IsActive)
                .HasDefaultValue(true);

            entity.Property(mt => mt.MergedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Relationships
            entity.HasOne<Table>()
                .WithMany()
                .HasForeignKey(mt => mt.ParentTableId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Table>()
                .WithMany()
                .HasForeignKey(mt => mt.ChildTableId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique constraint: Can't merge same tables twice
            entity.HasIndex(mt => new { mt.ParentTableId, mt.ChildTableId })
                .IsUnique();
        });

        // ═══════════════════════════════════════════════════
        // MENU CATEGORY CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<MenuCategory>(entity =>
        {
            entity.ToTable("MenuCategories");
            entity.HasKey(mc => mc.Id);

            entity.Property(mc => mc.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(mc => mc.DisplayOrder)
                .HasDefaultValue(0);

            entity.Property(mc => mc.IsActive)
                .HasDefaultValue(true);

            entity.Property(mc => mc.IsDeleted)
                .HasDefaultValue(false);

            entity.Property(mc => mc.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Relationships
            entity.HasOne<Restaurant>()
                .WithMany()
                .HasForeignKey(mc => mc.RestaurantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            entity.HasIndex(mc => mc.RestaurantId);
            entity.HasIndex(mc => mc.IsActive);
            entity.HasIndex(mc => mc.IsDeleted);
        });

        // ═══════════════════════════════════════════════════
        // MENU SUBCATEGORY CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<MenuSubCategory>(entity =>
        {
            entity.ToTable("MenuSubCategories");
            entity.HasKey(msc => msc.Id);

            entity.Property(msc => msc.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(msc => msc.DisplayOrder)
                .HasDefaultValue(0);

            entity.Property(msc => msc.IsActive)
                .HasDefaultValue(true);

            entity.Property(msc => msc.IsDeleted)
                .HasDefaultValue(false);

            // Relationships
            entity.HasOne<MenuCategory>()
                .WithMany()
                .HasForeignKey(msc => msc.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(msc => msc.CategoryId);
            entity.HasIndex(msc => msc.IsActive);
            entity.HasIndex(msc => msc.IsDeleted);
        });

        // ═══════════════════════════════════════════════════
        // MENU ITEM CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<MenuItem>(entity =>
        {
            entity.ToTable("MenuItems");
            entity.HasKey(mi => mi.Id);

            entity.Property(mi => mi.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(mi => mi.BasePrice)
                .HasColumnType("decimal(10,2)")
                .IsRequired();

            entity.Property(mi => mi.ImageUrl)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(mi => mi.ItemType)
                .HasMaxLength(20)
                .HasDefaultValue("regular");

            entity.Property(mi => mi.AvailabilityStatus)
                .HasMaxLength(20)
                .HasDefaultValue("available");

            entity.Property(mi => mi.PromoPrice)
                .HasColumnType("decimal(10,2)");

            entity.Property(mi => mi.DisplayOrder)
                .HasDefaultValue(0);

            entity.Property(mi => mi.IsAvailable)
                .HasDefaultValue(true);

            entity.Property(mi => mi.IsActive)
                .HasDefaultValue(true);

            entity.Property(mi => mi.IsDeleted)
                .HasDefaultValue(false);

            entity.Property(mi => mi.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(mi => mi.LastModifiedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Relationships
            entity.HasOne<Restaurant>()
                .WithMany()
                .HasForeignKey(mi => mi.RestaurantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<MenuSubCategory>()
                .WithMany()
                .HasForeignKey(mi => mi.SubCategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            entity.HasIndex(mi => mi.RestaurantId);
            entity.HasIndex(mi => mi.SubCategoryId);
            entity.HasIndex(mi => mi.IsAvailable);
            entity.HasIndex(mi => mi.IsActive);
            entity.HasIndex(mi => mi.IsDeleted);
            entity.HasIndex(mi => new { mi.RestaurantId, mi.IsAvailable, mi.IsActive });
        });

        // ═══════════════════════════════════════════════════
        // MENU ITEM VARIATION CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<MenuItemVariation>(entity =>
        {
            entity.ToTable("MenuItemVariations");
            entity.HasKey(miv => miv.Id);

            entity.Property(miv => miv.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(miv => miv.VariationValue)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(miv => miv.PriceModifier)
                .HasColumnType("decimal(10,2)")
                .HasDefaultValue(0);

            entity.Property(miv => miv.IsDefault)
                .HasDefaultValue(false);

            entity.Property(miv => miv.DisplayOrder)
                .HasDefaultValue(0);

            entity.Property(miv => miv.IsDeleted)
                .HasDefaultValue(false);

            // Relationships
            entity.HasOne<MenuItem>()
                .WithMany()
                .HasForeignKey(miv => miv.MenuItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(miv => miv.MenuItemId);
            entity.HasIndex(miv => miv.IsDeleted);
        });

        // ═══════════════════════════════════════════════════
        // COMBO ITEM CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<ComboItem>(entity =>
        {
            entity.ToTable("ComboItems");
            entity.HasKey(ci => ci.Id);

            entity.Property(ci => ci.Quantity)
                .HasDefaultValue(1);

            entity.Property(ci => ci.IsOptional)
                .HasDefaultValue(false);

            // Relationships
            entity.HasOne<MenuItem>()
                .WithMany()
                .HasForeignKey(ci => ci.ComboMenuItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<MenuItem>()
                .WithMany()
                .HasForeignKey(ci => ci.IncludedMenuItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            entity.HasIndex(ci => ci.ComboMenuItemId);
            entity.HasIndex(ci => ci.IncludedMenuItemId);
        });

        // ═══════════════════════════════════════════════════
        // INGREDIENT CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<Ingredient>(entity =>
        {
            entity.ToTable("Ingredients");
            entity.HasKey(i => i.Id);

            entity.Property(i => i.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(i => i.Unit)
                .HasMaxLength(50);

            entity.Property(i => i.CurrentStock)
                .HasColumnType("decimal(10,2)");

            entity.Property(i => i.MinimumStock)
                .HasColumnType("decimal(10,2)");

            entity.Property(i => i.IsAvailable)
                .HasDefaultValue(true);

            entity.Property(i => i.LastModifiedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Relationships
            entity.HasOne<Restaurant>()
                .WithMany()
                .HasForeignKey(i => i.RestaurantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique constraint: Ingredient name per restaurant
            entity.HasIndex(i => new { i.RestaurantId, i.Name })
                .IsUnique();

            // Indexes
            entity.HasIndex(i => i.RestaurantId);
            entity.HasIndex(i => i.IsAvailable);
        });

        // ═══════════════════════════════════════════════════
        // MENU ITEM INGREDIENT CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<MenuItemIngredient>(entity =>
        {
            entity.ToTable("MenuItemIngredients");
            entity.HasKey(mii => mii.Id);

            entity.Property(mii => mii.QuantityRequired)
                .HasColumnType("decimal(10,2)")
                .IsRequired();

            entity.Property(mii => mii.IsOptional)
                .HasDefaultValue(false);

            // Relationships
            entity.HasOne<MenuItem>()
                .WithMany()
                .HasForeignKey(mii => mii.MenuItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Ingredient>()
                .WithMany()
                .HasForeignKey(mii => mii.IngredientId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique constraint: MenuItem + Ingredient combination
            entity.HasIndex(mii => new { mii.MenuItemId, mii.IngredientId })
                .IsUnique();

            // Indexes
            entity.HasIndex(mii => mii.MenuItemId);
            entity.HasIndex(mii => mii.IngredientId);
        });

        // ═══════════════════════════════════════════════════
        // INGREDIENT ALTERNATIVE CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<IngredientAlternative>(entity =>
        {
            entity.ToTable("IngredientAlternatives");
            entity.HasKey(ia => ia.Id);

            entity.Property(ia => ia.PriceModifier)
                .HasColumnType("decimal(10,2)")
                .HasDefaultValue(0);

            entity.Property(ia => ia.AutoSubstitute)
                .HasDefaultValue(false);

            // Relationships
            entity.HasOne<Ingredient>()
                .WithMany()
                .HasForeignKey(ia => ia.OriginalIngredientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Ingredient>()
                .WithMany()
                .HasForeignKey(ia => ia.AlternativeIngredientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Restaurant>()
                .WithMany()
                .HasForeignKey(ia => ia.RestaurantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique constraint: Original + Alternative combination
            entity.HasIndex(ia => new { ia.OriginalIngredientId, ia.AlternativeIngredientId })
                .IsUnique();

            // Indexes
            entity.HasIndex(ia => ia.RestaurantId);
            entity.HasIndex(ia => ia.OriginalIngredientId);
            entity.HasIndex(ia => ia.AlternativeIngredientId);
        });

        // ═══════════════════════════════════════════════════
        // PRICE HISTORY CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<PriceHistory>(entity =>
        {
            entity.ToTable("PriceHistory");
            entity.HasKey(ph => ph.Id);

            entity.Property(ph => ph.PriceType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(ph => ph.OldPrice)
                .HasColumnType("decimal(10,2)")
                .IsRequired();

            entity.Property(ph => ph.NewPrice)
                .HasColumnType("decimal(10,2)")
                .IsRequired();

            entity.Property(ph => ph.Reason)
                .HasMaxLength(500);

            entity.Property(ph => ph.EffectiveDate)
                .IsRequired();

            entity.Property(ph => ph.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Relationships
            entity.HasOne<Restaurant>()
                .WithMany()
                .HasForeignKey(ph => ph.RestaurantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<MenuItem>()
                .WithMany()
                .HasForeignKey(ph => ph.MenuItemId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<IngredientAlternative>()
                .WithMany()
                .HasForeignKey(ph => ph.IngredientAlternativeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<MenuItemVariation>()
                .WithMany()
                .HasForeignKey(ph => ph.VariationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            entity.HasIndex(ph => ph.RestaurantId);
            entity.HasIndex(ph => ph.MenuItemId);
            entity.HasIndex(ph => ph.CreatedAt);
            entity.HasIndex(ph => ph.EffectiveDate);
        });

        // ═══════════════════════════════════════════════════
        // MENU ITEM ANALYTICS CONFIGURATION
        // ═══════════════════════════════════════════════════
        modelBuilder.Entity<MenuItemAnalytics>(entity =>
        {
            entity.ToTable("MenuItemAnalytics");
            entity.HasKey(mia => mia.Id);

            entity.Property(mia => mia.TotalRevenue)
                .HasColumnType("decimal(10,2)");

            entity.Property(mia => mia.AvgPrepTimeMinutes)
                .HasColumnType("decimal(5,2)");

            entity.Property(mia => mia.TimesOrdered)
                .HasDefaultValue(0);

            entity.Property(mia => mia.TimesModified)
                .HasDefaultValue(0);

            entity.Property(mia => mia.TimesOutOfStock)
                .HasDefaultValue(0);

            entity.Property(mia => mia.MorningOrders)
                .HasDefaultValue(0);

            entity.Property(mia => mia.AfternoonOrders)
                .HasDefaultValue(0);

            entity.Property(mia => mia.EveningOrders)
                .HasDefaultValue(0);

            entity.Property(mia => mia.NightOrders)
                .HasDefaultValue(0);

            // Relationships
            entity.HasOne<MenuItem>()
                .WithMany()
                .HasForeignKey(mia => mia.MenuItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Restaurant>()
                .WithMany()
                .HasForeignKey(mia => mia.RestaurantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique constraint: MenuItem + Date
            entity.HasIndex(mia => new { mia.MenuItemId, mia.AnalysisDate })
                .IsUnique();

            // Indexes
            entity.HasIndex(mia => mia.RestaurantId);
            entity.HasIndex(mia => mia.MenuItemId);
            entity.HasIndex(mia => mia.AnalysisDate);
        });

        // ═══════════════════════════════════════════════════
        // QUERY FILTERS (GLOBAL)
        // ═══════════════════════════════════════════════════

        // Soft delete filter for MenuCategories
        modelBuilder.Entity<MenuCategory>().HasQueryFilter(mc => !mc.IsDeleted);

        // Soft delete filter for MenuSubCategories
        modelBuilder.Entity<MenuSubCategory>().HasQueryFilter(msc => !msc.IsDeleted);

        // Soft delete filter for MenuItems
        modelBuilder.Entity<MenuItem>().HasQueryFilter(mi => !mi.IsDeleted);

        // Soft delete filter for MenuItemVariations
        modelBuilder.Entity<MenuItemVariation>().HasQueryFilter(miv => !miv.IsDeleted);
    }


    /// <summary>
    /// Override SaveChanges to automatically update timestamps
    /// </summary>
    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// Override SaveChangesAsync to automatically update timestamps
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Automatically update UpdatedAt timestamp for modified entities
    /// </summary>
    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            // Update UpdatedAt if property exists
            if (entry.Entity.GetType().GetProperty("UpdatedAt") != null)
            {
                entry.Property("UpdatedAt").CurrentValue = SystemClock.Instance.GetCurrentInstant();
            }
        }
    }
}
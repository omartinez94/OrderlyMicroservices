using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NodaTime;

namespace Catalog.API.Data;

public class RestaurantMigrationContext : DbContext
{
    public RestaurantMigrationContext(DbContextOptions<RestaurantMigrationContext> options) : base(options)
    {
    }
    
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Brand> Brands => Set<Brand>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

            entity.Property(r => r.BrandId)
                .IsRequired();

            entity.Property(r => r.AllowAutoSubstitute)
                .HasDefaultValue(false);

            entity.Property(r => r.AutoConfirmOrders)
                .HasDefaultValue(false);

            entity.Property(r => r.AutoConfirmReservations)
                .HasDefaultValue(false);

            entity.Property(r => r.EstimatedTurnoverMinutes)
                .HasDefaultValue(30);

            entity.HasIndex(r => r.BrandId);
        });

        modelBuilder.Entity<Brand>(entity =>
        {
            entity.ToTable("Brands");
            entity.HasKey(b => b.Id);

            entity.Property(b => b.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(b => b.Description)
                .HasMaxLength(500);

            entity.Property(b => b.LogoUrl)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(b => b.WebsiteUrl)
                .HasMaxLength(255);

            entity.Property(b => b.ContactEmail)
                .HasMaxLength(255);

            entity.Property(b => b.ContactPhone)
                .HasMaxLength(20);

            entity.Property(b => b.CuisineType)
                .HasMaxLength(50);

            entity.HasIndex(b => b.Name);
        });
        
        base.OnModelCreating(modelBuilder);
    }
}

public class RestaurantMigrationContextFactory : IDesignTimeDbContextFactory<RestaurantMigrationContext>
{
    public RestaurantMigrationContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RestaurantMigrationContext>();
        
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=catalogdb;Username=cataloguser;Password=catalogpassword",
            npgsqlOptions => 
            {
                npgsqlOptions.EnableRetryOnFailure();
                npgsqlOptions.UseNodaTime();
            });

        return new RestaurantMigrationContext(optionsBuilder.Options);
    }
}

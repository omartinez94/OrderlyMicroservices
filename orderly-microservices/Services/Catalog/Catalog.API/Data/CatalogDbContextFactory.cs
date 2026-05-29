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
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Restaurant>(entity =>
        {
            entity.ToTable("Restaurants");
            entity.HasKey(r => r.Id);
            
            entity.Ignore(r => r.CreatedAt);
            entity.Ignore(r => r.LastModifiedAt);
            entity.Ignore(r => r.CreatedBy);
            entity.Ignore(r => r.LastModifiedBy);
            entity.Ignore(r => r.IsActive);

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
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure());

        return new RestaurantMigrationContext(optionsBuilder.Options);
    }
}

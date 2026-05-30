namespace Ordering.Infrastructure.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => CustomerId.Of(value));

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();

        builder.Property(c => c.Email).HasMaxLength(255).IsRequired();

        builder.HasIndex(c => c.Email).IsUnique();

        builder.OwnsOne(c => c.Address, addressBuilder =>
        {
            addressBuilder.Property(a => a.Street).HasMaxLength(180);
            addressBuilder.Property(a => a.City).HasMaxLength(50);
            addressBuilder.Property(a => a.State).HasMaxLength(50);
            addressBuilder.Property(a => a.ZipCode).HasMaxLength(5);
            addressBuilder.Property(a => a.Country).HasMaxLength(50);
        });
    }
}

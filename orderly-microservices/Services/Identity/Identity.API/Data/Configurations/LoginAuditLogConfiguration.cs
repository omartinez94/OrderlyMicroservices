namespace Identity.API.Data.Configurations;

public class LoginAuditLogConfiguration : IEntityTypeConfiguration<LoginAuditLog>
{
    public void Configure(EntityTypeBuilder<LoginAuditLog> builder)
    {
        builder.ToTable("LoginAuditLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.EventType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(l => l.IpAddress)
            .IsRequired()
            .HasMaxLength(45);

        builder.Property(l => l.UserAgent)
            .HasMaxLength(500);

        builder.Property(l => l.Timestamp)
            .IsRequired();

        builder.HasIndex(l => l.UserId);
        builder.HasIndex(l => l.Timestamp);
    }
}

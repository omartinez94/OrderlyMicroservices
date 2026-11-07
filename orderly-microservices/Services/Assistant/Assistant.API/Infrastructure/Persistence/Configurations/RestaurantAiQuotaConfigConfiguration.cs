using Assistant.API.Domain.Entities;

namespace Assistant.API.Infrastructure.Persistence.Configurations;

public sealed class RestaurantAiQuotaConfigConfiguration : IEntityTypeConfiguration<RestaurantAiQuotaConfig>
{
    public void Configure(EntityTypeBuilder<RestaurantAiQuotaConfig> builder)
    {
        builder.ToTable("restaurant_ai_quota_configs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RestaurantId)
            .IsRequired();

        builder.HasIndex(x => x.RestaurantId)
            .IsUnique()
            .HasDatabaseName("IX_restaurant_ai_quota_configs_RestaurantId");

        builder.Property(x => x.IncludedTokensPerCycle)
            .IsRequired()
            .HasDefaultValue(RestaurantAiQuotaConfig.DefaultIncludedTokensPerCycle);

        builder.Property(x => x.AllowOverage)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.MaxOverageTokens)
            .IsRequired()
            .HasDefaultValue(0L);

        builder.Property(x => x.MaxOverageSpendUsd)
            .HasPrecision(12, 4)
            .IsRequired()
            .HasDefaultValue(0.00m);

        builder.Property(x => x.AutoPurchasePacksEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.PackSizeTokens)
            .IsRequired()
            .HasDefaultValue(RestaurantAiQuotaConfig.DefaultPackSizeTokens);

        builder.Property(x => x.PricePerPackUsd)
            .HasPrecision(12, 4)
            .IsRequired()
            .HasDefaultValue(RestaurantAiQuotaConfig.DefaultPricePerPackUsd);

        builder.Property(x => x.IsSuspended)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.SuspensionReason)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.LastModifiedAt);

        builder.Property(x => x.LastModifiedBy)
            .HasMaxLength(200);

        builder.Ignore(x => x.LastModified);
    }
}

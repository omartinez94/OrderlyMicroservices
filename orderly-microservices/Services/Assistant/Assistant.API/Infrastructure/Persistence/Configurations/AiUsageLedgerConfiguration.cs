using Assistant.API.Domain.Entities;

namespace Assistant.API.Infrastructure.Persistence.Configurations;

public sealed class AiUsageLedgerConfiguration : IEntityTypeConfiguration<AiUsageLedger>
{
    public void Configure(EntityTypeBuilder<AiUsageLedger> builder)
    {
        builder.ToTable("ai_usage_ledgers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RestaurantId)
            .IsRequired();

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.ConversationId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.OperationType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Provider)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Model)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.PromptTokens)
            .IsRequired();

        builder.Property(x => x.CompletionTokens)
            .IsRequired();

        builder.Property(x => x.CachedTokens)
            .IsRequired();

        builder.Property(x => x.EffectiveBillableTokens)
            .IsRequired();

        builder.Property(x => x.EstimatedCostUsd)
            .HasPrecision(14, 6)
            .IsRequired();

        builder.Property(x => x.BillingCycle)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.WasOverage)
            .IsRequired();

        builder.Property(x => x.RecordedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.RestaurantId, x.BillingCycle })
            .HasDatabaseName("IX_ai_usage_ledgers_RestaurantId_BillingCycle");

        builder.HasIndex(x => x.RecordedAt)
            .HasDatabaseName("IX_ai_usage_ledgers_RecordedAt");
    }
}

using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.API.Domain.Entities;

/// <summary>
/// Configures tenant AI token allowances, opt-in overage safety ceilings, auto-purchase packs,
/// and suspension status per restaurant.
/// </summary>
public sealed class RestaurantAiQuotaConfig : Entity<Guid>, ITenantEntity, IAuditableEntity
{
    public const long DefaultIncludedTokensPerCycle = 250_000;
    public const int DefaultPackSizeTokens = 100_000;
    public const decimal DefaultPricePerPackUsd = 2.00m;

    public Guid RestaurantId { get; set; }

    // Quotas (Effective Billable Tokens)
    public long IncludedTokensPerCycle { get; set; } = DefaultIncludedTokensPerCycle;
    public bool AllowOverage { get; set; }
    public long MaxOverageTokens { get; set; }
    public decimal MaxOverageSpendUsd { get; set; }

    // Auto-Purchase Packs
    public bool AutoPurchasePacksEnabled { get; set; }
    public int PackSizeTokens { get; set; } = DefaultPackSizeTokens;
    public decimal PricePerPackUsd { get; set; } = DefaultPricePerPackUsd;

    // Suspension
    public bool IsSuspended { get; set; }
    public string? SuspensionReason { get; set; }

    // Audit Metadata (NodaTime Instant)
    public Instant CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Instant? LastModifiedAt { get; set; }
    public string LastModifiedBy { get; set; } = string.Empty;

    [NotMapped]
    public Instant? LastModified
    {
        get => LastModifiedAt;
        set => LastModifiedAt = value;
    }

    public static RestaurantAiQuotaConfig CreateDefault(Guid restaurantId, Instant now, string actor = "system")
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ArgumentException("RestaurantId must be non-empty.", nameof(restaurantId));
        }

        RestaurantAiQuotaConfig config = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            IncludedTokensPerCycle = DefaultIncludedTokensPerCycle,
            AllowOverage = false,
            MaxOverageTokens = 0,
            MaxOverageSpendUsd = 0.00m,
            AutoPurchasePacksEnabled = false,
            PackSizeTokens = DefaultPackSizeTokens,
            PricePerPackUsd = DefaultPricePerPackUsd,
            IsSuspended = false,
            CreatedAt = now,
            CreatedBy = actor,
            LastModifiedAt = now,
            LastModifiedBy = actor
        };

        return config;
    }

    public void UpdateSafetyCeiling(
        bool allowOverage,
        long maxOverageTokens,
        decimal maxOverageSpendUsd,
        bool autoPurchasePacksEnabled,
        string actor,
        Instant now)
    {
        if (maxOverageTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOverageTokens), "MaxOverageTokens cannot be negative.");
        }

        if (maxOverageSpendUsd < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOverageSpendUsd), "MaxOverageSpendUsd cannot be negative.");
        }

        AllowOverage = allowOverage;
        MaxOverageTokens = allowOverage ? maxOverageTokens : 0;
        MaxOverageSpendUsd = allowOverage ? maxOverageSpendUsd : 0.00m;
        AutoPurchasePacksEnabled = allowOverage && autoPurchasePacksEnabled;
        ModifiedFrom(actor, now);
    }

    public void CreatedFrom(string userId, Instant timestamp)
    {
        CreatedBy = userId;
        CreatedAt = timestamp;
        LastModifiedBy = userId;
        LastModifiedAt = timestamp;
    }

    public void ModifiedFrom(string userId, Instant timestamp)
    {
        LastModifiedBy = userId;
        LastModifiedAt = timestamp;
    }
}

using System.ComponentModel.DataAnnotations;

namespace Discount.Grpc.Options;

/// <summary>
/// Strongly-typed configuration for the Discount.Grpc microservice.
/// Bind via <c>builder.Services.AddOptions&lt;DiscountOptions&gt;().Bind(...).ValidateDataAnnotations().ValidateOnStart();</c>
/// in <c>Program.cs</c>. Defaults reflect the v1.4 / v1.5 plan-text
/// resolutions (see <c>DISCOUNT_SERVICE_PLAN.md §6.7</c> + the v1.4
/// changelog M-L9 entry for <see cref="OutboxDeadLetterThreshold"/>).
/// </summary>
/// <remarks>
/// <para>
/// The flag-gated <see cref="EnableFeedbackSubmittedConsumer"/>,
/// <see cref="EnableDiscountAppliedPublishing"/>, etc. default to
/// <c>false</c>: each lights up only after the corresponding upstream or
/// downstream service lands. (FeedbackSubmittedConsumer),
/// (deferred architecture publishes), and
/// (OrderCreatedConsumer + DiscountAppliedIntegrationEvent v2) flip
/// individual flags at their own commit; flipping multiple flags in one
/// deploy is the operator's call.
/// </para>
/// <para>
/// <see cref="OutboxDeadLetterThreshold"/> = 5 (not 0) per v1.4 changelog
/// M-L9 — fail-closed on first poison message would take Discount offline
/// on day-1. Production alert-and-let-humans-triage: 5 dead-rows is
/// loud enough to surface in monitoring without a single message
/// tripping /ready.
/// </para>
/// </remarks>
public sealed class DiscountOptions
{
    public const string SectionName = "Discount";

    /// <summary>
    /// Master switch for the <c>DiscountExpirySweepService</c> hosted
    /// service. Mirrors the <c>DiscountExpirySweep:Enabled</c> knob in
    /// <c>DiscountExpirySweepOptions</c> so the sweep can be turned off
    /// independently of the rest of the options. Kept here for the
    /// production / config-validation surface; the actual service reads
    /// <c>DiscountExpirySweepOptions</c> directly.
    /// </summary>
    public bool SweepEnabled { get; set; } = true;

    /// <summary>
    /// Sweep interval in minutes (5–1440). Captured in this options
    /// class so it participates in <c>ValidateOnStart()</c>; the actual
    /// <c>PeriodicTimer</c> in <c>DiscountExpirySweepService</c> reads the
    /// service-local <c>DiscountExpirySweepOptions</c>.
    /// </summary>
    [Range(1, 1440)]
    public int SweepIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Threshold for the <c>/ready</c> outbox-dead-letter probe. When
    /// the count of rows in <c>outbox_messages_dead</c> exceeds this
    /// number, the probe goes Unhealthy and the LB pulls Discount from
    /// rotation. Default 5.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int OutboxDeadLetterThreshold { get; set; } = 5;

    /// <summary>
    /// Publishes <c>DiscountHistoryAppendedIntegrationEvent</c> from
    /// Coupon / RewardCode / DiscountRule CUD paths. Default
    /// <c>true</c> so production lands ready for Catalog's consumer.
    /// </summary>
    public bool EnableHistoryPublishing { get; set; } = true;

    /// <summary>
    /// Consumes <c>MenuItemChangedIntegrationEvent</c> from Catalog and
    /// re-evaluates active DiscountRules. Default <c>true</c> so the
    /// consumer wires up automatically when the bus configuration lands.
    /// Set to <c>false</c> to disable at runtime without
    /// removing the consumer from the bus topology.
    /// </summary>
    public bool EnableMenuItemChangedConsumer { get; set; } = true;

    /// <summary>
    /// Consumes <c>RestaurantConfigurationChangedIntegrationEvent</c>
    /// from Catalog. Same flip-the-flag semantics as
    /// <see cref="EnableMenuItemChangedConsumer"/>.
    /// </summary>
    public bool EnableRestaurantConfigChangedConsumer { get; set; } = true;

    /// <summary>
    /// Consumes <c>FeedbackSubmittedIntegrationEvent</c> from
    /// Notification v1 (which doesn't ship in this plan's window).
    /// Default <c>false</c>; flips on when Notification v1's publisher
    /// lands. Wiring is via MassTransit's conditional
    /// <c>AddConsumer&lt;FeedbackSubmittedConsumer&gt;()</c> idiom per
    /// plan §0.4.5 v1.1 H5.
    /// </summary>
    public bool EnableFeedbackSubmittedConsumer { get; set; } = false;

    /// <summary>
    /// Publishes <c>DiscountAppliedIntegrationEvent</c> from
    /// <c>RedeemDiscount</c>. Default <c>false</c> until the cross-service
    /// consumer lands. Wire flag — defaults fail-secure.
    /// </summary>
    public bool EnableDiscountAppliedPublishing { get; set; } = false;

    /// <summary>
    /// Publishes <c>RewardGeneratedIntegrationEvent</c> from
    /// <c>RewardCode.CreateRewardCode</c>. Default <c>false</c>.
    /// </summary>
    public bool EnableRewardGeneratedPublishing { get; set; } = false;

    /// <summary>
    /// Publishes <c>RewardRedeemedIntegrationEvent</c> from
    /// <c>RedeemRewardCode</c>. Default <c>false</c>.
    /// </summary>
    public bool EnableRewardRedeemedPublishing { get; set; } = false;

    /// <summary>
    /// Consumes <c>OrderCreatedIntegrationEvent</c> from Ordering
    /// (which doesn't ship in this plan's window); flips
    /// on when Ordering's publisher lands. Same conditional-registration
    /// pattern as <see cref="EnableFeedbackSubmittedConsumer"/>.
    /// </summary>
    public bool EnableOrderCreatedConsumer { get; set; } = false;

    /// <summary>
    /// Future-proofing knob for multi-currency baskets. When
    /// set, <c>BuildingBlocks.Discounts.ApplyDiscountsHelper.Apply</c>
    /// pins the rounding for the configured currency rather than the
    /// basket's currency claim. <c>null</c> means "use the basket's
    /// pinned currency". Today this is unused.
    /// </summary>
    public string? AppliedDiscountCurrency { get; set; }
}

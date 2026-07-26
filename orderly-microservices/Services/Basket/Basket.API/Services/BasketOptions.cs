using System.ComponentModel.DataAnnotations;

namespace Basket.API.Services;

/// <summary>
/// Configuration for the cart-lifecycle services owned by Basket. Bound
/// from the <c>Basket</c> section of <c>appsettings.json</c> through
/// <see cref="Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.AddOptions{T}"/>
/// at host startup.
/// </summary>
public sealed class BasketOptions
{
    /// <summary>Configuration section name used by the binder.</summary>
    public const string SectionName = "Basket";

    /// <summary>
    /// Hosted-service group that walks the Marten <c>mt_doc_basket</c>
    /// collection for carts whose <c>ExpiresAt</c> is in the past and
    /// deletes them (no event publish — the cart is abandoned, not
    /// checked out). Defaults to a 5-minute sweep interval.
    /// </summary>
    public ExpirySweepOptions ExpirySweep { get; init; } = new();

    /// <summary>
    /// Options for the expiry-sweep hosted service. Lives as a nested
    /// class so the operator faces a single <c>Basket</c> section in
    /// <c>appsettings.json</c> rather than two top-level keys.
    /// </summary>
    public sealed class ExpirySweepOptions
    {
        /// <summary>
        /// Master switch. When <c>false</c> the sweep hosted service
        /// short-circuits at startup and never touches the store. Useful
        /// in test environments where the cart never expires.
        /// </summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// Time between sweep ticks. Default 5 minutes — same cadence
        /// Discount.Grpc uses for its <c>DiscountExpirySweepService</c>
        /// Must be greater than zero;
        /// <see cref="ValidationAttribute"/> enforces it.
        /// </summary>
        [Required]
        [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
        public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Maximum number of baskets the sweep deletes per tick. Caps
        /// the size of any single <c>SaveChangesAsync</c> transaction so
        /// a long-untouched tenant cannot starve the rest of the queue.
        /// The next tick continues with whatever escaped.
        /// </summary>
        [Range(1, 100_000)]
        public int BatchSize { get; init; } = 1_000;
    }
}

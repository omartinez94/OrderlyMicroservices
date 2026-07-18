using System.ComponentModel.DataAnnotations;

namespace Basket.API.ProblemDetails;

/// <summary>
/// Configuration for the operator-owned URIs carried in the
/// <c>type</c> field of RFC 7807 <c>application/problem+json</c>
/// responses. Bound from the <c>Basket:Problems</c> configuration
/// section via <see cref="IOptionsMonitor{TOptions}"/> so a change
/// to <c>Basket__Problems__BaseUrl</c> propagates to the next
/// request without a redeploy (or even a process restart).
/// </summary>
/// <remarks>
/// <para>RFC 7807 §3.1 says the <c>type</c> URI is the canonical
/// machine-readable identifier for a problem type — clients
/// pattern-match on it for error handling. The URI SHOULD be
/// human-readable, SHOULD NOT change between releases (so cached
/// clients keep working), and is conventionally operator-owned
/// (the canonical RFC 7807 example uses
/// <c>https://example.com/probs/out-of-credit</c>).</para>
/// <para>By defaulting <see cref="BaseUrl"/> to a placeholder
/// (<c>https://orderly.io/problems/</c>) and binding it via
/// <see cref="IOptionsMonitor{TOptions}"/>, the operator can:</para>
/// <list type="bullet">
/// <item>Point at <c>https://docs.orderly.io/problems/</c> in
/// production via env var (no redeploy).</item>
/// <item>A/B test URI stability by switching between two URLs and
/// watching client traffic.</item>
/// <item>Re-route a deprecated slug to a new one without touching
/// code: e.g., <c>/problems/too-many-requests</c> → <c>/problems/v2/too-many-requests</c>.</item>
/// </list>
/// </remarks>
public sealed class BasketProblemDetailsOptions
{
    public const string SectionName = "Basket:Problems";

    /// <summary>
    /// Base URL for the operator-owned <c>type</c> URI in every
    /// ProblemDetails response. Problem slugs are appended to this
    /// value (with a leading-slash check); the trailing slash is
    /// mandatory. The single source of truth is
    /// <c>appsettings.json</c> (key <c>Basket:Problems:BaseUrl</c>);
    /// the env var <c>Basket__Problems__BaseUrl</c> overrides it.
    /// <see cref="IOptionsMonitor{TOptions}"/> reads fresh on every
    /// request so a config change propagates without a redeploy.
    /// </summary>
    /// <remarks>
    /// No inline C# default — by design. The class is shape-only;
    /// the actual value lives in <c>appsettings.json</c>. If neither
    /// appsettings.json nor the env var provides a value,
    /// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> returns
    /// <c>null</c> and the first ProblemDetails emission throws a
    /// <see cref="NullReferenceException"/>. The
    /// <see cref="RequiredAttribute"/> + <see cref="ValidateOnStart"/>
    /// pair turns this into a boot-time configuration error instead
    /// of a runtime null-deref.
    /// </remarks>
    [Required(ErrorMessage = "Basket:Problems:BaseUrl is required (set in appsettings.json or via Basket__Problems__BaseUrl env var).")]
    [RegularExpression(@"^https?://.+/[^/]+/$",
        ErrorMessage = "Basket:Problems:BaseUrl must be a URL ending with a trailing slash (e.g., 'https://orderly.io/problems/').")]
    public string BaseUrl { get; set; } = default!;
}

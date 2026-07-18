namespace BuildingBlocks.Behaviors;

/// <summary>
/// Payment Card Industry (as in PCI-DSS, the Payment Card Industry Data Security Standard).
/// Marker attribute that signals <see cref="LoggingBehavior{TRequest,TResponse}"/>
/// to redact the request payload from structured log lines. Apply to
/// any command or query whose payload carries PII (names, addresses,
/// email) or PCI (card numbers, CVV, expiration). The behavior logs
/// only the type name in the request-data slot — the payload itself
/// stays out of every sink.
/// </summary>
/// <remarks>
/// <para>The attribute lookup is cached per-type on first read
/// (<see cref="LoggingBehavior{TRequest,TResponse}"/> hot path is
/// invoked on every request, so the reflection call would otherwise
/// repeat for the same <c>TRequest</c> on every invocation).</para>
/// <para>Apply the attribute to the <em>command</em> type
/// (<c>CheckoutBasketCommand</c>, not the inner
/// <c>BasketCheckoutDto</c>). The behavior reads the attribute from
/// the type passed into the pipeline, which is the command record.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class PciSensitiveAttribute : Attribute
{
}
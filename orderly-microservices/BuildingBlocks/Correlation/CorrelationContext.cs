namespace BuildingBlocks.Correlation;

/// <summary>
/// Ambient source for the per-request correlation id, propagated via
/// <see cref="AsyncLocal{T}"/> so the domain layer can stamp it on rows
/// (e.g. <c>OrderActivity.CorrelationId</c>) without threading it through
/// every method signature.
/// </summary>
/// <remarks>
/// <para>
/// The id is set by <c>BuildingBlocks.Behaviors.LoggingBehavior</c> on every
/// HTTP request (read from the <c>X-Correlation-Id</c> request header or a
/// fresh <see cref="Guid"/>) and by MassTransit bus consumers (read from
/// <c>ConsumeContext.CorrelationId</c>). It is cleared in a
/// <c>try/finally</c> at the end of every pipeline so the value cannot leak
/// across requests on the same logical call context.
/// </para>
/// <para>
/// <see cref="Set"/> and <see cref="Clear"/> are <c>internal</c> so only
/// BuildingBlocks (and the <c>LoggingBehavior</c>) can write the value;
/// <see cref="Current"/> is <c>public</c> because the domain layer needs to
/// read it.
/// </para>
/// </remarks>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _current = new();

    /// <summary>
    /// The ambient correlation id, or <c>null</c> if no
    /// request/bus scope has set it (e.g. background work).
    /// </summary>
    public static string? Current => _current.Value;

    internal static void Set(string id) => _current.Value = id;

    internal static void Clear() => _current.Value = null;
}
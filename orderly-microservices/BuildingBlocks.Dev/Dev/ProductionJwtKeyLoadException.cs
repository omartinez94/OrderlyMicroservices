namespace BuildingBlocks.Dev;

/// <summary>
/// Raised by <see cref="DevJwtBearerFallbackExtensions.AddJwtAuthenticationWithDevFallback"/>
/// when <c>JWT_SECRET</c> is set in a non-Development environment.
/// </summary>
/// <remarks>
/// <para>The dev HS256 fallback is signed with a symmetric key read from
/// <c>JWT_SECRET</c>. Shipping that to production would expose the same
/// signing material that issues tokens, which is the opposite of what
/// the OpenIddict RS256 path gives us. This exception is the
/// fail-closed signal: when an operator leaks <c>JWT_SECRET</c> into a
/// staging or production deploy, every host that calls the extension
/// refuses to start rather than silently accepting forgeable
/// HS256-signed admin tokens.</para>
/// <para>Caught by the global exception handler
/// (<c>BuildingBlocks.Exceptions.Handler.CustomExceptionHandler</c>);
/// rendered as a 500 with a stable <c>traceId</c>. The boot-time
/// startup exception path means the host dies before listening on its
/// socket; the operator sees the message in the container logs and
/// fixes the leak rather than discovering the exposure at runtime.</para>
/// </remarks>
/// <param name="message">
/// Operator-facing remediation. Must name the environment and the
/// env var so the fix is obvious from the exception text alone.
/// </param>
public sealed class ProductionJwtKeyLoadException(string message) : InvalidOperationException(message)
{
}

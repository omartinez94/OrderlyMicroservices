namespace Identity.API.Extensions;

/// <summary>
/// Thrown at registration time when a non-Development environment
/// references an OpenIddict signing or encryption certificate that
/// cannot be located or loaded.
/// </summary>
/// <remarks>
/// <para>This exception is the fail-closed signal for Phase 2 of the
/// <c>TRUST_ROOT_HARDENING_PLAN.md</c>: a non-Development host that
/// lacks a configured certificate refuses to start rather than booting
/// without a signing key (which would silently invalidate every token
/// OpenIddict later tried to issue).</para>
/// <para>Caught by the global exception handler in
/// <c>BuildingBlocks/Exceptions/Handler/CustomExceptionHandler.cs</c>;
/// rendered as a 500 with the underlying <see cref="Exception.Message"/>
/// in the <c>traceId</c> field for ops to copy-paste into the
/// remediation runbook.</para>
/// </remarks>
public sealed class OpenIddictCertificateLoadException : InvalidOperationException
{
    public OpenIddictCertificateLoadException(string message) : base(message) { }

    public OpenIddictCertificateLoadException(string message, Exception innerException)
        : base(message, innerException) { }
}

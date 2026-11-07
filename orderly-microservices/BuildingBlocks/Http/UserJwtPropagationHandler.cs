using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Http;

/// <summary>
/// Delegating handler that propagates the caller's inbound <c>Authorization: Bearer &lt;jwt&gt;</c>
/// header and stamps <c>X-Agent-Invocation: true</c> on direct internal microservice HTTP calls
/// made by the AI Assistant, avoiding a YARP loopback hop while preserving downstream RBAC and
/// <c>ICurrentRestaurantProvider</c> tenant isolation.
/// </summary>
public sealed class UserJwtPropagationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    public const string AgentInvocationHeaderName = "X-Agent-Invocation";

    private static readonly AsyncLocal<string?> AmbientBearerToken = new();

    /// <summary>
    /// Attaches an explicit JWT bearer token for detached background scopes where no active
    /// <see cref="HttpContext"/> exists.
    /// </summary>
    public static IDisposable AttachBearerToken(string bearerToken)
    {
        string? previous = AmbientBearerToken.Value;
        AmbientBearerToken.Value = bearerToken;
        return new TokenScope(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Remove(AgentInvocationHeaderName);
        request.Headers.TryAddWithoutValidation(AgentInvocationHeaderName, "true");

        string? rawAuthHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(rawAuthHeader))
        {
            if (AuthenticationHeaderValue.TryParse(rawAuthHeader, out AuthenticationHeaderValue? parsedHeader))
            {
                request.Headers.Authorization = parsedHeader;
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Authorization", rawAuthHeader);
            }
        }
        else if (!string.IsNullOrWhiteSpace(AmbientBearerToken.Value))
        {
            string token = AmbientBearerToken.Value!;
            request.Headers.Authorization = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? AuthenticationHeaderValue.Parse(token)
                : new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class TokenScope(string? previous) : IDisposable
    {
        public void Dispose()
        {
            AmbientBearerToken.Value = previous;
        }
    }
}

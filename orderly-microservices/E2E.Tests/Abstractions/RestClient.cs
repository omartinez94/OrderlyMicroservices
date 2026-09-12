using System.Net.Http.Headers;

namespace E2E.Tests.Abstractions;

/// <summary>
/// Thin HTTP wrapper that pins <c>Accept: application/json</c> and
/// attaches the bearer token from the supplied <see cref="DevJwtMinter"/>
/// on every request. Each service exposes its own endpoints via the
/// YARP gateway in production; in the E2E docker-compose override the
/// tests call the services directly so a single test failure points at
/// one container, not a gateway hop.
/// </summary>
public sealed class RestClient
{
    private readonly HttpClient _http;

    public RestClient(HttpClient http) => _http = http;

    /// <summary>
    /// Sends an HTTP request with the supplied JSON body and token,
    /// optionally asserting a 2xx response.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Convenience: GET a JSON response and deserialize it.
    /// </summary>
    public async Task<T?> GetJsonAsync<T>(string path, string bearerToken, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, body: null, bearerToken, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }
}

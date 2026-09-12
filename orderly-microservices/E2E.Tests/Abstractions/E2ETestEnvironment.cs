using Polly;
using Polly.Retry;

namespace E2E.Tests.Abstractions;

/// <summary>
/// Single-purpose bootstrap for the E2E suite. Asserts every service
/// is up (via <c>/live</c>) before any test runs, then hands out the
/// per-service <see cref="RestClient"/>s the tests use. Failures
/// surface as <see cref="SkipException"/> with a clear message
/// pointing at <c>scripts/up-e2e.ps1</c> so a developer who forgot to
/// start the stack sees an actionable error rather than a confusing
/// 502.
/// </summary>
/// <remarks>
/// <para><b>Why <c>/live</c> not <c>/ready</c>:</b> the readiness probe
/// bounces during cold backing-store restarts and is the wrong gate
/// for "the service is up enough to take HTTP requests". Phase 5 of
/// <c>PERSISTENCE_AND_RELIABILITY_PLAN.md</c> split the two endpoints
/// specifically so this kind of "give the stack a minute to settle"
/// gate is reliable.</para>
/// <para><b>Retry policy:</b> 30 attempts × 2 s = up to 60 s. Matches
/// the docker-compose healthcheck <c>retries</c> + <c>start_period</c>
/// budget for a cold stack (60-90 s for MSSQL). The first failure
/// shows the offending URL + status code in the skip message.</para></remarks>
public sealed class E2ETestEnvironment : IAsyncLifetime
{
    private readonly string _jwtSecret;

    public E2ETestEnvironment()
    {
        // JWT_SECRET is supplied by docker-compose.e2e.yml; the host
        // reads it from the E2E process env. The fallback to "dev-secret"
        // mirrors the default the docker-compose override sets for the
        // API services — using the same value here keeps test + host in
        // sync without a separate config file.
        _jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? throw new InvalidOperationException(
                "JWT_SECRET must be set before running E2E tests. " +
                "The docker-compose.e2e.yml override sets it automatically; " +
                "if you're running tests against a manually-launched stack, " +
                "export the same secret your services use.");

        Catalog = new RestClient(new HttpClient { BaseAddress = new Uri(ServiceEndpoints.Catalog) });
        Basket = new RestClient(new HttpClient { BaseAddress = new Uri(ServiceEndpoints.Basket) });
        Ordering = new RestClient(new HttpClient { BaseAddress = new Uri(ServiceEndpoints.Ordering) });
        Kitchen = new RestClient(new HttpClient { BaseAddress = new Uri(ServiceEndpoints.Kitchen) });
        Identity = new RestClient(new HttpClient { BaseAddress = new Uri(ServiceEndpoints.Identity) });

        JwtMinter = new DevJwtMinter(_jwtSecret);
    }

    public RestClient Catalog { get; }

    public RestClient Basket { get; }

    public RestClient Ordering { get; }

    public RestClient Kitchen { get; }

    public RestClient Identity { get; }

    public DevJwtMinter JwtMinter { get; }

    /// <summary>
    /// Service URLs the readiness gate pings. Ordered so a failure
    /// surfaces the most upstream service first (identity → catalog →
    /// basket → discount → ordering → kitchen → gateway).
    /// </summary>
    private static readonly (string Name, string Base)[] ReadinessTargets =
    [
        ("identity", ServiceEndpoints.Identity),
        ("catalog", ServiceEndpoints.Catalog),
        ("basket", ServiceEndpoints.Basket),
        ("ordering", ServiceEndpoints.Ordering),
        ("kitchen", ServiceEndpoints.Kitchen),
    ];

    public async ValueTask InitializeAsync()
    {
        // Resilience pipeline: 30 × 2 s = up to 60 s, with full jitter
        // so the first probe lands a little after the service's
        // /live endpoint accepts traffic. Without jitter all 5
        // containers retry at the same moment and stampede the stack.
        AsyncRetryPolicy<HttpResponseMessage> policy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .OrResult(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                retryCount: 30,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(2000 + Random.Shared.Next(500)),
                onRetry: (outcome, delay, attempt, _) =>
                {
                    Console.Error.WriteLine(
                        $"[E2E bootstrap] {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString() ?? "?"} " +
                        $"— retry {attempt}/30 in {delay.TotalMilliseconds:F0} ms");
                });

        foreach (var (name, baseUrl) in ReadinessTargets)
        {
            await policy.ExecuteAsync(async ct =>
            {
                using HttpClient client = new() { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(2) };
                using HttpResponseMessage response = await client.GetAsync("/live", ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return response;
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Marker exception used by the readiness gate to short-circuit a
/// test class when the docker-compose stack isn't up. xUnit's
/// <see cref="Xunit.Sdk.SkipException"/> keeps the suite green
/// (rather than red) so the missing-stack scenario doesn't pollute
/// CI status. The "Other" feedback directory at
/// <c>scripts/up-e2e.ps1</c> tells the developer the next step.
/// </summary>
public sealed class E2EStackDownException : Exception
{
    public E2EStackDownException(string message) : base(message)
    {
    }
}

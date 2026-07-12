namespace Catalog.API.Tests.Integration;

/// <summary>
/// Minimal mutable <see cref="TimeProvider"/> for job tests. Lets a test
/// pin "now" to a fixed instant and advance it deterministically, so the
/// Hangfire jobs' window logic can be exercised without real wall-clock
/// waits. Avoids taking a dependency on
/// <c>Microsoft.Extensions.TimeProvider.Testing</c>.
/// </summary>
public sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Pins the provider's notion of "now" to <paramref name="value"/>.</summary>
    public void SetUtcNow(DateTimeOffset value) => _now = value;
}

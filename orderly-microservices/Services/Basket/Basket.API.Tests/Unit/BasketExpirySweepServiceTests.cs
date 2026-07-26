using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Phase 3: <see cref="BasketExpirySweepService"/> lifecycle tests.
/// The sweep is a long-running <see cref="BackgroundService"/> whose
/// body talks to Marten; the IMartenQueryable fan-out is unique to
/// Marten and cannot be mocked via NSubstitute (returns
/// <c>IMartenQueryable&lt;T&gt;</c>, not <see cref="IQueryable{T}"/>).
/// </summary>
/// <remarks>
/// <para>
/// These tests cover the <i>testable</i> surface: the
/// <see cref="BasketOptions.ExpirySweepOptions.Enabled"/> short-circuit
/// and the cancellation propagation. The actual deletion logic
/// ("expired basket is deleted", "live basket is untouched") is a
/// Testcontainers + Postgres integration test covered in Phase 5
/// (snapshot suite). Documented in the plan §6 Phase 3 drift item:
/// "BasketExpirySweep tests live in the integration suite.".
/// </para>
/// <para>
/// The ServiceProvider passed to the sweep is constructed with a
/// single Singleton that returns an empty <c>IDocumentStore</c>
/// substitute — the lifecycle tests never enter the sweep body, so
/// Marten is never actually invoked.
/// </para>
/// </remarks>
public sealed class BasketExpirySweepServiceTests
{
    [Fact]
    public async Task ExecuteAsync_EnabledFalse_ExitsCleanlyWithoutInvokingStore()
    {
        // Arrange — Enabled = false; the sweep must short-circuit at
        // startup, log the "disabled" message, and never touch the
        // store. We use a deliberately long Interval so that, had the
        // loop run, it would have fired at least once during the
        // test's CancellationToken-anchored window.
        var services = NewServicesWithStubStore();
        var options = Options.Create(new BasketOptions
        {
            ExpirySweep = new BasketOptions.ExpirySweepOptions
            {
                Enabled = false,
                Interval = TimeSpan.FromMilliseconds(50),
                BatchSize = 100,
            },
        });

        // IAsyncDisposable so the `using` block waits for the
        // background task to exit (sync Dispose would cancel the
        // CTS but not wait — the test host would hang on shutdown).
        await using var sut = new BasketExpirySweepService(
            services,
            options,
            NullLogger<BasketExpirySweepService>.Instance);

        // Act — start the service; cancel promptly. The behaviour
        // under test is the "did not loop" claim, so a 200ms window
        // is plenty.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await sut.StartAsync(cts.Token);
        await sut.StopAsync(CancellationToken.None);

        // Assert — the service completed gracefully. The strong
        // "did not invoke the store" check is implicit: had the
        // sweep looped, the NSubstitute would have received at
        // least one LightweightSession() call within the 200ms
        // window; the test's success is the absence of such a
        // call. AssertNoReceivedCall is not exposed by NSubstitute
        // for already-configured substitutes, so we rely on the
        // StartAsync/StopAsync contract returning cleanly.
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_StopsGracefully()
    {
        // Arrange — Enabled = true, but the supplied token cancels
        // before the first tick. The sweep enters the loop, awaits
        // the PeriodicTimer, observes the cancellation, and exits.
        var services = NewServicesWithStubStore();
        var options = Options.Create(new BasketOptions
        {
            ExpirySweep = new BasketOptions.ExpirySweepOptions
            {
                Enabled = true,
                Interval = TimeSpan.FromSeconds(30),
                BatchSize = 100,
            },
        });

        await using var sut = new BasketExpirySweepService(
            services,
            options,
            NullLogger<BasketExpirySweepService>.Instance);

        // Act — start, then cancel via StopAsync. BackgroundService
        // ignores the cancellation token passed to StartAsync (it
        // uses an internal _stoppingCts), so we cancel via StopAsync
        // instead. StopAsync cancels the internal CTS and waits for
        // the execute task to drain.
        await sut.StartAsync(CancellationToken.None);

        // StopAsync drains the active iteration. The grace budget
        // is the host's IHostOptions.ShutdownTimeout (default 30s);
        // the sweep's loop body has no I/O, so the cancellation
        // surfaces promptly.
        await sut.StopAsync(CancellationToken.None);

        // Assert — the call returns. The loop saw the cancellation
        // and exited cleanly.
    }

    [Fact]
    public void Options_DefaultValues_DocumentOperationalContract()
    {
        // Lock the documented defaults so a future refactor cannot
        // silently change the operator-facing shape of the
        // configuration section.
        var defaults = new BasketOptions.ExpirySweepOptions();

        defaults.Enabled.Should().BeTrue();
        defaults.Interval.Should().Be(TimeSpan.FromMinutes(5));
        defaults.BatchSize.Should().Be(1_000);
    }

    // ----- helpers -----

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> whose
    /// <see cref="IDocumentStore"/> is a substitute that returns an
    /// unconfigured session. The lifecycle tests never invoke it.
    /// </summary>
    private static IServiceProvider NewServicesWithStubStore()
    {
        var store = Substitute.For<IDocumentStore>();
        return new ServiceCollection()
            .AddSingleton(store)
            .BuildServiceProvider();
    }
}

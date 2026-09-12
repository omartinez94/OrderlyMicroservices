using Microsoft.Extensions.Logging;
using Ordering.Infrastructure.Services;

namespace Ordering.Infrastructure.Tests.Services;

/// <summary>
/// Locks in the dev-only <see cref="DailyReconciliationRunner"/>
/// contract. The production implementation is a deliberate placeholder
/// (see the runner's XML doc comment): it logs that the dev trigger
/// fired and returns <c>0</c> — the real reconciliation algorithm
/// ships with the future ordering-scheduler plan. The MCP server's
/// <c>trigger_scheduled_jobs</c> tool calls this path unchanged.
/// </summary>
public sealed class DailyReconciliationRunnerTests
{
    /// <summary>
    /// Happy path: <see cref="DailyReconciliationRunner.RunAsync"/>
    /// completes synchronously, returns <c>0</c>, and emits a single
    /// info-level log line naming the dev endpoint. Spec
    /// <c>ExecuteAsync_RunsReconciliation_AndLogsResult</c>.
    /// </summary>
    [Fact]
    public async Task RunAsync_HappyPath_LogsAndReturnsZero()
    {
        ILogger<DailyReconciliationRunner> logger = Substitute.For<ILogger<DailyReconciliationRunner>>();
        var runner = new DailyReconciliationRunner(logger);

        int result = await runner.RunAsync(CancellationToken.None);

        result.Should().Be(0);
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Daily reconciliation")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    /// <summary>
    /// <see cref="DailyReconciliationRunner.RunAsync"/> accepts a
    /// <see cref="CancellationToken"/> and completes without throwing
    /// even when the token is pre-cancelled. The placeholder
    /// implementation never observes the token, so a future
    /// implementation that respects it must continue to satisfy the
    /// "never throw on cancellation alone" half of this contract; the
    /// "observe the token" half is the replacement for the spec's
    /// <c>ExecuteAsync_CancellationRequested_StopsGracefully</c> (which
    /// is not assertable against the placeholder behaviour).
    /// </summary>
    [Fact]
    public async Task RunAsync_PreCancelledToken_StillCompletesCleanly()
    {
        ILogger<DailyReconciliationRunner> logger = Substitute.For<ILogger<DailyReconciliationRunner>>();
        var runner = new DailyReconciliationRunner(logger);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task<int>> act = () => runner.RunAsync(cts.Token);

        await act.Should().NotThrowAsync();
        int result = await act();
        result.Should().Be(0);
    }

    /// <summary>
    /// Idempotency: the spec's <c>ExecuteAsync_Disabled_ExitsCleanly</c>
    /// has no production counterpart — the runner has no
    /// configuration-driven "disabled" flag (unlike the outbox
    /// dispatcher which gates on <c>OutboxOptions.Enabled</c>).
    /// Replaced with "repeated invocation is idempotent and observable
    /// in the log" — the production callers (the dev trigger endpoint,
    /// the future Hangfire job) may invoke it on every scheduler tick,
    /// and a regression that suppressed the log or short-circuited
    /// silently would mask a stuck scheduler.
    /// </summary>
    [Fact]
    public async Task RunAsync_RepeatedInvocation_LogsEachCallAndReturnsZero()
    {
        ILogger<DailyReconciliationRunner> logger = Substitute.For<ILogger<DailyReconciliationRunner>>();
        var runner = new DailyReconciliationRunner(logger);

        int first = await runner.RunAsync(CancellationToken.None);
        int second = await runner.RunAsync(CancellationToken.None);
        int third = await runner.RunAsync(CancellationToken.None);

        first.Should().Be(0);
        second.Should().Be(0);
        third.Should().Be(0);

        logger.Received(3).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}

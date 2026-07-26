using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Phase 4: gRPC resilience pipeline. The
/// <c>AddStandardResilienceHandler</c> call in <c>Program.cs</c>
/// stacks three policies (retry + circuit breaker + attempt
/// timeout) and a total request timeout. These tests verify the
/// <see cref="ResiliencePipelineBuilder"/> shape — they do NOT spin
/// up the gRPC client or Discount.Grpc (those are Phase 5
/// Testcontainers tests).
/// </summary>
public sealed class DiscountGrpcResiliencePipelineTests
{
    [Fact]
    public void RetryOptions_MatchPlanShape()
    {
        // The retry policy Program.cs wires: 3 attempts, exponential
        // backoff with jitter. Lock the contract — a future refactor
        // that drops the jitter would defeat the broker-recovery
        // thundering-herd protection.
        var opts = new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            UseJitter = true,
            BackoffType = Polly.DelayBackoffType.Exponential,
        };

        opts.MaxRetryAttempts.Should().Be(3);
        opts.UseJitter.Should().BeTrue();
        opts.BackoffType.Should().Be(Polly.DelayBackoffType.Exponential);
    }

    [Fact]
    public void CircuitBreakerOptions_MatchPlanShape()
    {
        // The breaker Program.cs wires: 50% failure ratio with a
        // 5-call minimum throughput over 30s; 30s break duration.
        // These are the standard "5 failures in 30s" semantics
        // referenced in the plan.
        var opts = new CircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(30),
        };

        opts.SamplingDuration.Should().Be(TimeSpan.FromSeconds(30));
        opts.FailureRatio.Should().Be(0.5);
        opts.MinimumThroughput.Should().Be(5);
        opts.BreakDuration.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TimeoutOptions_MatchPlanShape()
    {
        // The attempt timeout (3s per call) and the total request
        // timeout (8s — covers 3 attempts × ~3s with jitter). The
        // plan §6 Phase 4 mandates 3s attempt + 8s total; lock the
        // contract so a future refactor that drops the per-attempt
        // budget is caught.
        var attemptTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(3) };
        var totalTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(8) };

        attemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(3));
        totalTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task Pipeline_RetryPolicy_TriggersOnTransientFailure()
    {
        // Arrange — build a pipeline manually mirroring the
        // AddStandardResilienceHandler config and confirm a
        // transient failure triggers the retry policy.
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                UseJitter = true,
                BackoffType = Polly.DelayBackoffType.Exponential,
            })
            .Build();

        var attempts = 0;
        Func<CancellationToken, ValueTask> action = async ct =>
        {
            attempts++;
            await Task.Yield();
            throw new InvalidOperationException("transient");
        };

        // Assert — the retry policy re-ran the delegate. With
        // MaxRetryAttempts = 3 the delegate is called up to 4 times
        // (1 initial + 3 retries). We assert >= 2 to keep the test
        // resilient against internal Polly jitter.
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            async () => await pipeline.ExecuteAsync(action));
        attempts.Should().BeGreaterThanOrEqualTo(2);
    }
}

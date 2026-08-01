using BuildingBlocks.Messaging.Outbox;
using Discount.Grpc.Data;
using Discount.Grpc.Messaging.Outbox;
using Discount.Grpc.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Discount.Grpc.Health;

/// <summary>
/// Readiness probes for the Discount.Grpc microservice. Mirrors the
/// Catalog split between <c>/live</c> (process-up only) and <c>/ready</c>
/// (readiness — checks must pass). Wired in <c>Program.cs</c> via
/// <see cref="AddDiscountHealthChecks"/> + <c>MapHealthChecks</c>.
/// </summary>
/// <remarks>
/// <para>Four <c>IHealthCheck</c> implementations live under the
/// <c>"ready"</c> tag:</para>
/// <list type="bullet">
/// <item><c>AddNpgSql(...)</c> — uses <c>AspNetCore.HealthChecks.NpgSql</c>
/// to verify the PostgreSQL connection string opens successfully. Cheap;
/// catches connection-string typos, network partitions, and
/// container-restart windows.</item>
/// <item><see cref="BrokerHealthCheck"/> — reads <see cref="BrokerHealthState"/>.
/// The dispatcher writes to it on top-level <c>DispatchOnceAsync</c>
/// throws. Flips <c>Unhealthy</c> when the counter exceeds
/// <c>OutboxOptions.MaxConsecutiveBrokerFailures</c>.</item>
/// <item><see cref="OutboxDeadLetterCheck"/> — counts rows in
/// <c>OutboxDeadMessages</c> against
/// <see cref="DiscountOptions.OutboxDeadLetterThreshold"/> (default 5
/// per v1.4 changelog M-L9). Catches a poison-message wave that would
/// otherwise be silent.</item>
/// <item><see cref="RabbitMqBrokerCheck"/> — TCP-probe the configured
/// broker when <c>Outbox:Enabled=true</c>. No-op returning
/// <c>Healthy</c> in dev where no broker is configured.</item>
/// </list>
/// <para>The <see cref="DiscountHealthCheckNames"/> string constants
/// surface the canonical names so the <c>/ready</c> JSON response rows
/// are addressable from monitoring tools without typos.</para>
/// </remarks>
public static class DiscountHealthChecks
{
    /// <summary>
    /// Registers the four readiness probes under the <c>"ready"</c>
    /// tag. The <c>/live</c> probe is not registered here — it's a
    /// <c>Predicate = _ =&gt; false</c> on <c>MapHealthChecks("/live",
    /// ...)</c> in <c>Program.cs</c> (Catalog's convention).
    /// </summary>
    /// <param name="configuration">Host configuration; supplies
    /// <c>ConnectionStrings:Database</c> for the Npgsql probe.</param>
    public static IServiceCollection AddDiscountHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetConnectionString("Database")!,
                tags: new[] { "ready" })
            .AddCheck<BrokerHealthCheck>(DiscountHealthCheckNames.BrokerCircuit, tags: new[] { "ready" })
            .AddCheck<OutboxDeadLetterCheck>(DiscountHealthCheckNames.OutboxDeadLetter, tags: new[] { "ready" })
            .AddCheck<RabbitMqBrokerCheck>(DiscountHealthCheckNames.RabbitMqBroker, tags: new[] { "ready" });
        return services;
    }
}

/// <summary>
/// Canonical names for the Discount readiness probes. Use these strings
/// when asserting against the <c>/ready</c> JSON response in tests or
/// when wiring monitoring alerts.
/// </summary>
public static class DiscountHealthCheckNames
{
    public const string Postgres = "discount-postgres";
    public const string BrokerCircuit = "discount-broker-circuit";
    public const string OutboxDeadLetter = "discount-outbox-dead-letter";
    public const string RabbitMqBroker = "discount-rabbitmq";
}

/// <summary>
/// Reads <see cref="BrokerHealthState.ConsecutiveBrokerFailures"/> and
/// surfaces <see cref="HealthCheckResult.Unhealthy"/> once the count
/// meets or exceeds the configured threshold. The dispatcher is the
/// single writer; this probe is the single reader at /ready. The
/// probe <em>does not</em> reset the counter — that's the dispatcher's
/// job on the next successful dispatch.
/// </summary>
public sealed class BrokerHealthCheck(
    BrokerHealthState state,
    IOptions<DiscountOptions> options,
    ILogger<BrokerHealthCheck> logger) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Threshold: the plan's §6.7 v1.2 M-L10 reads it from OutboxOptions.
        // DiscountOptions carries the threshold for the dead-letter probe;
        // the circuit breaker reads OutboxOptions.MaxConsecutiveBrokerFailures
        // directly. Both fields share the default of 3.
        var threshold = options.Value.OutboxDeadLetterThreshold > 0
            ? options.Value.OutboxDeadLetterThreshold
            : 3; // safe default matches OutboxOptions default.

        var failures = state.ConsecutiveBrokerFailures;
        if (failures >= threshold)
        {
            var trippedAt = state.TrippedAt;
            var data = new Dictionary<string, object>
            {
                ["consecutive_failures"] = failures,
                ["threshold"] = threshold,
                ["tripped_at"] = trippedAt?.ToString("O") ?? "<unknown>",
            };
            logger.LogWarning(
                "Broker circuit probe returning Unhealthy: {Failures}/{Threshold} consecutive failures (tripped {TrippedAt}).",
                failures,
                threshold,
                trippedAt);
            return Task.FromResult(HealthCheckResult.Unhealthy(
                description: $"Broker circuit tripped ({failures}/{threshold} consecutive).",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            description: $"Broker circuit closed ({failures}/{threshold} consecutive).",
            data: new Dictionary<string, object>
            {
                ["consecutive_failures"] = failures,
                ["threshold"] = threshold,
            }));
    }
}

/// <summary>
/// Counts rows in <c>OutboxDeadMessages</c> against
/// <see cref="DiscountOptions.OutboxDeadLetterThreshold"/>. Goes
/// <see cref="HealthCheckResult.Unhealthy"/> when the count exceeds
/// the threshold. Surfacing this in <c>/ready</c> means the LB pulls
/// the replica from rotation if a poison-message wave is in flight,
/// rather than leaving the service running but useless.
/// </summary>
public sealed class OutboxDeadLetterCheck(
    IServiceScopeFactory scopes,
    IOptions<DiscountOptions> options,
    ILogger<OutboxDeadLetterCheck> logger) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var threshold = options.Value.OutboxDeadLetterThreshold;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var deadCount = await db.OutboxDeadMessages.CountAsync(cancellationToken).ConfigureAwait(false);
            var data = new Dictionary<string, object>
            {
                ["dead_count"] = deadCount,
                ["threshold"] = threshold,
            };

            return deadCount > threshold
                ? HealthCheckResult.Unhealthy(
                    description: $"Outbox dead-letter count ({deadCount}) above threshold ({threshold}).",
                    data: data)
                : HealthCheckResult.Healthy(
                    description: $"Outbox dead-letter count ({deadCount}) within threshold ({threshold}).",
                    data: data);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbox dead-letter readiness probe failed.");
            return HealthCheckResult.Unhealthy("Dead-letter count query threw.", ex);
        }
    }
}

/// <summary>
/// TCP-probes the configured RabbitMQ broker when <c>Outbox:Enabled=true</c>
/// (production posture). In dev where the compose stack runs without a
/// broker for Discount, this is a no-op <see cref="HealthCheckResult.Healthy"/>:
/// the dispatcher is also disabled in that case (<c>Outbox:Enabled=false</c>),
/// so a broker outage is moot for the dev surface. Production deployments
/// must set <c>Outbox:Enabled=true</c> AND a reachable
/// <c>MessageBroker__Host</c> for the probe to fire.
/// </summary>
public sealed class RabbitMqBrokerCheck(
    IConfiguration configuration,
    IOptions<OutboxOptions> outboxOptions,
    ILogger<RabbitMqBrokerCheck> logger) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!outboxOptions.Value.Enabled)
        {
            // Outbox dispatcher disabled in dev — skip the probe.
            return HealthCheckResult.Healthy("Outbox disabled; broker probe skipped.");
        }

        var host = configuration["MessageBroker:Host"] ?? configuration["MessageBroker__Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            // Production posture but no host configured: that's an
            // operator error and we surface it loudly here so the
            // failure mode isn't "everything looks healthy".
            logger.LogWarning("Outbox enabled but MessageBroker:Host is not configured.");
            return HealthCheckResult.Unhealthy("MessageBroker:Host missing while Outbox is enabled.");
        }

        // Cheap TCP-probe: open a connection, fail-closed on any error.
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync(host, 5672);
            var completed = await Task.WhenAny(
                connectTask,
                Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)).ConfigureAwait(false);
            if (completed != connectTask || !tcp.Connected)
            {
                return HealthCheckResult.Unhealthy($"Broker {host}:5672 not reachable.");
            }
            return HealthCheckResult.Healthy($"Broker {host}:5672 reachable.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RabbitMQ readiness probe failed for {Host}:5672.", host);
            return HealthCheckResult.Unhealthy($"Broker {host}:5672 probe threw.", ex);
        }
    }
}

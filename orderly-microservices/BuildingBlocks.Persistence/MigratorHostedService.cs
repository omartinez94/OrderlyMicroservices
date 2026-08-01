using System.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BuildingBlocks.Persistence;

/// <summary>
/// Generic <see cref="IHostedService"/> that applies pending EF Core migrations
/// at host startup with exponential-backoff retry on transient failures.
/// Replaces the inline <c>await MigrateAsync()</c> blocks every relational
/// service used to inline before <c>app.Run()</c>.
/// </summary>
/// <remarks>
/// <para>Models itself on <c>BuildingBlocks.Messaging.Outbox.OutboxDispatcher&lt;TContext&gt;</c>:
/// the same constructor signature, the same <c>protected abstract CreateContext</c>
/// override point, the same per-replica scope isolation. Override
/// <see cref="CreateContext"/> to resolve the adopter's <typeparamref name="TContext"/>
/// from a fresh <see cref="IServiceScope"/>.</para>
/// <para>Retry semantics: a transient failure (per <see cref="IsTransient"/>) backs
/// off by <see cref="MigratorHostedServiceOptions.InitialBackoffSeconds"/>,
/// doubling each attempt up to <see cref="MigratorHostedServiceOptions.MaxBackoffSeconds"/>,
/// until either <see cref="MigratorHostedServiceOptions.MaxAttempts"/> is reached or
/// <see cref="MigratorHostedServiceOptions.MigrationTimeoutSeconds"/> elapses.
/// Non-transient exceptions fail the host immediately — they indicate a
/// schema/program mismatch that retrying cannot fix.</para>
/// </remarks>
public abstract class MigratorHostedService<TContext> : IHostedService
    where TContext : DbContext
{
    private readonly IServiceProvider _services;
    private readonly MigratorHostedServiceOptions _options;
    private readonly ILogger<MigratorHostedService<TContext>> _logger;

    protected MigratorHostedService(
        IServiceProvider services,
        IOptions<MigratorHostedServiceOptions> options,
        ILogger<MigratorHostedService<TContext>> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a fresh <typeparamref name="TContext"/> from the supplied
    /// per-attempt scope. Mirrors <c>OutboxDispatcher.CreateContext</c>.
    /// </summary>
    protected abstract TContext CreateContext(IServiceProvider services);

    /// <summary>Apply pending migrations at host startup.</summary>
    public virtual async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Migrator hosted service disabled via MigratorHostedServiceOptions.Enabled = false.");
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(_options.MigrationTimeoutSeconds);
        var attempt = 0;
        var currentBackoff = TimeSpan.FromSeconds(_options.InitialBackoffSeconds);

        while (true)
        {
            attempt++;

            try
            {
                await using var scope = _services.CreateAsyncScope();
                var ctx = CreateContext(scope.ServiceProvider);
                await ctx.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Migrator: applied pending migrations on attempt {Attempt} in {Elapsed}.",
                    attempt,
                    DateTimeOffset.UtcNow - startedAt);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Host is shutting down — propagate.
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                var elapsed = DateTimeOffset.UtcNow - startedAt;

                if (attempt >= _options.MaxAttempts)
                {
                    _logger.LogError(ex,
                        "Migrator: exhausted MaxAttempts={MaxAttempts} after {Elapsed}.",
                        _options.MaxAttempts, elapsed);
                    throw;
                }

                if (elapsed >= timeout)
                {
                    _logger.LogError(ex,
                        "Migrator: exhausted MigrationTimeoutSeconds={Timeout}s after {Elapsed}.",
                        _options.MigrationTimeoutSeconds, elapsed);
                    throw;
                }

                // Randomised jitter (0..1s) to avoid thundering-herd retries
                // when multiple replicas boot at the same time.
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * 1000);
                var delay = currentBackoff + jitter;

                _logger.LogWarning(ex,
                    "Migrator: attempt {Attempt} failed ({Exception}); retrying in {Delay}.",
                    attempt, ex.GetType().Name, delay);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                // Exponential growth, capped.
                var nextSeconds = Math.Min(
                    currentBackoff.TotalSeconds * _options.BackoffMultiplier,
                    _options.MaxBackoffSeconds);
                currentBackoff = TimeSpan.FromSeconds(nextSeconds);
            }
        }
    }

    /// <summary>No-op — migrations already ran at startup.</summary>
    public virtual Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Classifies an exception as transient (worth retrying) or fatal.
    /// Postgres: any <see cref="PostgresException"/> with a transient
    /// <c>SqlState</c> — the engine surface area is broad; we accept
    /// any <c>PostgresException</c> here and let the timeout cap the
    /// retry window.
    /// SQL Server: <see cref="SqlException"/> numbers 1801 (database
    /// copy error), 4060 (database unavailable), 40613 (server busy),
    /// 233 (connection error), -2 (timeout).
    /// </summary>
    protected static bool IsTransient(Exception ex) => ex switch
    {
        PostgresException => true,
        SqlException sql => sql.Number is 1801 or 4060 or 40613 or 233 or -2,
        _ => false,
    };
}
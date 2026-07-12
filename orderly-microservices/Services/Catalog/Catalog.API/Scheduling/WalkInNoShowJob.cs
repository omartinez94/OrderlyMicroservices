using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Scheduling;

/// <summary>
/// Hangfire recurring job: marks <see cref="WalkInQueueStatus.Notified"/>
/// walk-in parties as <see cref="WalkInQueueStatus.NoShow"/> when the
/// 10-minute response window expires without a <c>SeatedAt</c>. Frees
/// any held table back to <see cref="TableStatus.Available"/>. Self-gates
/// on <c>CatalogScheduledJobs</c>.
/// </summary>
/// <remarks>
/// Cadence is configurable via <see cref="HangfireOptions.WalkInNoShowCron"/>
/// (default every minute). The 10-minute response window matches the plan
/// (Phase 5, Walk-in queue 10-minute response window) and architecture.md
/// §933-937 (walk-in party notification → 10 min → no-show).
/// </remarks>
public sealed class WalkInNoShowJob(
    IServiceScopeFactory scopeFactory,
    IFeatureManager featureManager,
    TimeProvider clock,
    IOptions<HangfireOptions> options,
    ILogger<WalkInNoShowJob> logger)
{
    private readonly HangfireOptions _options = options.Value;

    /// <summary>Walk-in response window after notification.</summary>
    private static readonly Duration WalkInResponseWindow = Duration.FromMinutes(10);

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 60, 120 })]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!await featureManager.IsEnabledAsync("CatalogScheduledJobs", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var now = clock.GetUtcNow();
        var nowInstant = Instant.FromDateTimeUtc(now.UtcDateTime);
        var threshold = nowInstant.Minus(WalkInResponseWindow);

        var candidates = await dbContext.WalkInQueues
            .Where(w => w.Status == WalkInQueueStatus.Notified
                && w.SeatedAt == null
                && w.NotifiedAt != null)
            .OrderBy(w => w.NotifiedAt)
            .Take(_options.MaxRowsPerTick)
            .ToListAsync(cancellationToken);

        var noShows = 0;
        foreach (var walkIn in candidates)
        {
            if (walkIn.NotifiedAt is null || walkIn.NotifiedAt.Value > threshold)
            {
                continue;
            }

            walkIn.Status = WalkInQueueStatus.NoShow;

            // Free the held table (same pattern as ReservationNoShowJob).
            if (walkIn.TableId.HasValue)
            {
                var table = await dbContext.Tables
                    .FirstOrDefaultAsync(t => t.Id == walkIn.TableId.Value, cancellationToken);
                if (table is not null && table.Status == TableStatus.Reserved)
                {
                    table.Status = TableStatus.Available;
                }
            }

            noShows++;
        }

        if (noShows > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "WalkInNoShowJob transitioned {Count} walk-in parties to NoShow", noShows);
        }
    }
}
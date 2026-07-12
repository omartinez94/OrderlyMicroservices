using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Scheduling;

/// <summary>
/// Hangfire recurring job: marks confirmed-but-not-seated reservations as
/// <see cref="ReservationStatus.NoShow"/> 15 minutes after the reservation
/// time. Free the held table (if any) back to <see cref="TableStatus.Available"/>.
/// Self-gates on <c>CatalogScheduledJobs</c>.
/// </summary>
/// <remarks>
/// Cadence is configurable via <see cref="HangfireOptions.ReservationNoShowCron"/>
/// (default every minute). The 15-minute grace window matches the architecture
/// doc §933-937 reservation/block window.
/// </remarks>
public sealed class ReservationNoShowJob(
    IServiceScopeFactory scopeFactory,
    IFeatureManager featureManager,
    TimeProvider clock,
    IOptions<HangfireOptions> options,
    ILogger<ReservationNoShowJob> logger)
{
    private readonly HangfireOptions _options = options.Value;

    /// <summary>
    /// Grace period after the reservation time before the row is auto-closed
    /// as a no-show. Matches <c>architecture.md</c>.
    /// </summary>
    private static readonly Duration NoShowGrace = Duration.FromMinutes(15);

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
        var threshold = nowInstant.Minus(NoShowGrace);

        // Load candidate reservations: Confirmed + ReservationTime + 15m <= now
        // + never seated. Anchored on UTC (same simplification as ReservationReminderJob).
        var candidates = await dbContext.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed
                && r.SeatedAt == null)
            .OrderBy(r => r.ReservationDate)
            .ThenBy(r => r.ReservationTime)
            .Take(_options.MaxRowsPerTick)
            .ToListAsync(cancellationToken);

        var noShows = 0;
        foreach (var reservation in candidates)
        {
            var reservationInstant = reservation.ReservationDate
                .AtStartOfDayInZone(DateTimeZone.Utc)
                .Plus(Duration.FromTicks(reservation.ReservationTime.TickOfDay))
                .ToInstant();

            if (reservationInstant > threshold)
            {
                // Reservation time is still within the grace window — skip.
                continue;
            }

            reservation.Status = ReservationStatus.NoShow;
            reservation.CancelledAt = nowInstant;

            // Free the held table if one was assigned at booking.
            if (reservation.TableId.HasValue)
            {
                var table = await dbContext.Tables
                    .FirstOrDefaultAsync(t => t.Id == reservation.TableId.Value, cancellationToken);
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
                "ReservationNoShowJob transitioned {Count} reservations to NoShow", noShows);
        }
    }
}
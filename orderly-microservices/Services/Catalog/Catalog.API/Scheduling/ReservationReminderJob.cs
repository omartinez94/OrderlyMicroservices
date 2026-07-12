using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Scheduling;

/// <summary>
/// Hangfire recurring job: publishes <see cref="ReservationReminderDueIntegrationEvent"/>
/// for every confirmed reservation that is exactly one hour away. Self-gates
/// on the <c>CatalogScheduledJobs</c> feature flag.
/// </summary>
/// <remarks>
/// Cadence is configurable via <see cref="HangfireOptions.ReservationReminderCron"/>
/// (default <c>*/5 * * * *</c> — every 5 minutes). The "exactly one hour away"
/// predicate is implemented as a 5-minute sliding window so a 5-minute cadence
/// still catches every reservation; with a coarser cadence the operator should
/// lengthen the window rather than rely on Hangfire's drift correction.
/// </remarks>
public sealed class ReservationReminderJob(
    IServiceScopeFactory scopeFactory,
    IFeatureManager featureManager,
    TimeProvider clock,
    IOptions<HangfireOptions> options,
    ILogger<ReservationReminderJob> logger)
{
    private readonly HangfireOptions _options = options.Value;

    /// <summary>
    /// Hangfire invocation entry point. <c>Hangfire.RecurringJob.AddOrUpdate</c>
    /// resolves the job via DI and calls this method on every tick.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 60, 120 })]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!await featureManager.IsEnabledAsync("CatalogScheduledJobs", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        var now = clock.GetUtcNow();
        var nowInstant = Instant.FromDateTimeUtc(now.UtcDateTime);
        var windowStart = nowInstant.Minus(Duration.FromMinutes(55));
        var windowEnd = nowInstant.Plus(Duration.FromMinutes(5));

        // The 55–5 minute window is the "is the reservation exactly one hour away"
        // window expressed in UTC instants. Combines ReservationDate + ReservationTime
        // (LocalDate / LocalTime on the row) into an Instant by anchoring on UTC
        // (the plan keeps everything in UTC; per-restaurant TimeZone.
        var dueReservations = await dbContext.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed
                && !r.ReminderSent
                && r.SeatedAt == null)
            .OrderBy(r => r.ReservationDate)
            .ThenBy(r => r.ReservationTime)
            .Take(_options.MaxRowsPerTick)
            .ToListAsync(cancellationToken);

        var dispatched = 0;
        foreach (var reservation in dueReservations)
        {
            var reservationInstant = reservation.ReservationDate
                .AtStartOfDayInZone(DateTimeZone.Utc)
                .Plus(Duration.FromTicks(reservation.ReservationTime.TickOfDay))
                .ToInstant();

            // Reservation reminder fires when the reservation time is
            // between [windowStart, windowEnd) — i.e. between 55 minutes
            // and 65 minutes from now.
            if (reservationInstant < windowStart || reservationInstant >= windowEnd)
            {
                continue;
            }

            await outbox.PublishAsync(new ReservationReminderDueIntegrationEvent
            {
                ReservationId = reservation.Id,
                RestaurantId = reservation.RestaurantId,
                ReservationNumber = reservation.ReservationNumber,
                CustomerName = reservation.CustomerName,
                CustomerPhone = reservation.CustomerPhone,
                CustomerEmail = reservation.CustomerEmail,
                ReservationDate = reservation.ReservationDate,
                ReservationTime = reservation.ReservationTime,
                PartySize = reservation.PartySize,
            }, cancellationToken).ConfigureAwait(false);

            reservation.ReminderSent = true;
            reservation.ReminderSentAt = nowInstant;
            dispatched++;
        }

        if (dispatched > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "ReservationReminderJob dispatched {Count} reminders (window {WindowStart:O} → {WindowEnd:O})",
                dispatched, windowStart, windowEnd);
        }
    }
}
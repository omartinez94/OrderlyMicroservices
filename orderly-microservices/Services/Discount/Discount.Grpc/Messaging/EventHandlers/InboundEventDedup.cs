using BuildingBlocks.Messaging.Events;
using Discount.Grpc.Data;
using Discount.Grpc.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Discount.Grpc.Messaging.EventHandlers;

/// <summary>
/// Shared idempotency primitive for Discount's MassTransit consumers.
/// Tries to insert a <see cref="ProcessedInboundevent"/> row keyed by
/// <c>(EventId, ConsumerType)</c>; the unique-key violation on
/// <see cref="SqliteException.SqlState"/> == <c>"1555"</c> is the
/// "already processed" signal. The handler swallows that exception and
/// returns <c>true</c> from <see cref="TryRecordAsync"/>, telling the
/// caller to skip the work.
/// </summary>
/// <remarks>
/// <para>This is the "otherwise gate on processed_inbound_events"
/// branch of the project's idempotency choice matrix. The unique-key
/// dedup pattern survives process restarts and bus redeliveries — the
/// composite PK is the contract; in-process dictionaries are not.</para>
/// </remarks>
internal static class InboundEventDedup
{
    public static async Task<bool> TryRecordAsync(
        DiscountContext db,
        Guid eventId,
        string consumerType,
        CancellationToken cancellationToken = default)
    {
        var entry = await db.ProcessedInboundevents.AddAsync(
            new ProcessedInboundevent
            {
                EventId = eventId,
                ConsumerType = consumerType,
                ConsumedAt = DateTime.UtcNow,
            },
            cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return false; // record was new; caller proceeds with the work.
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Detach the attempted insert so the DbContext's tracker
            // doesn't trip an "added-but-uncommitted" state on later
            // SaveChanges calls in the same scope.
            entry.State = EntityState.Detached;
            return true; // duplicate; caller skips the work.
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.Sqlite.SqliteException sqlite
            && sqlite.SqlState is "1555" or "2067" /* SQLITE_CONSTRAINT_PRIMARYKEY | UNIQUE */;

    /// <summary>Reads the <see cref="IntegrationEvent.Id"/> from an
    /// inbound event via reflection — keeps the dedup helper free of
    /// generic-type traffic noise per consumer type.</summary>
    public static Guid EventId(IntegrationEvent evt) => evt.Id;
}

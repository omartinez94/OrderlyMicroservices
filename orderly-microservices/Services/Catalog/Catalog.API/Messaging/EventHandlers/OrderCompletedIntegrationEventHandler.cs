using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Messaging.EventHandlers;

/// <summary>
/// MassTransit <see cref="IConsumer{T}"/> for
/// <see cref="OrderCompletedIntegrationEvent"/>. Updates
/// <see cref="MenuItemAnalytics"/> keyed by
/// <c>(MenuItemId, AnalysisDate = UTC date)</c>. Idempotent on
/// <c>(OrderId, MenuItemId)</c> via the <see cref="ProcessedOrderItem"/>
/// insert-then-fail-fast gate.
/// </summary>
/// <remarks>
/// <para><b>Why not use the unique index on MenuItemAnalytics.</b> The
/// table's natural key is <c>(MenuItemId, AnalysisDate)</c>, but the
/// idempotency contract is on <c>(OrderId, MenuItemId)</c>:
/// two <c>OrderCompleted</c> events for the same menu item on the same
/// day must both increment <c>TimesOrdered</c>/<c>TotalRevenue</c>.
/// Therefore the dedup table carries the order id explicitly.</para>
/// <para><b>Why a try/catch on <see cref="DbUpdateException"/>.</b>
/// Postgres surfaces a unique-violation as <c>PostgresException</c>
/// with <c>SqlState == "23505"</c> wrapped in <c>DbUpdateException</c>;
/// catching that is the standard "INSERT-then-fail" idempotency
/// pattern (mirrors the <c>processed_order_items</c> pattern).</para>
/// </remarks>
public sealed class OrderCompletedIntegrationEventHandler(
    CatalogDbContext dbContext,
    ILogger<OrderCompletedIntegrationEventHandler> logger)
    : IConsumer<OrderCompletedIntegrationEvent>
{
    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<OrderCompletedIntegrationEvent> context)
    {
        var message = context.Message;
        var analysisDate = LocalDate.FromDateTime(message.CompletedAt.ToDateTimeUtc());

        // Wrap the BeginTransactionAsync + commit cycle in
        // Database.CreateExecutionStrategy().ExecuteAsync(...) so
        // EnableRetryOnFailure(5, 10s) on the CatalogDbContext (added
        // in Catalog.API/Program.cs) doesn't crash with "The configured
        // execution strategy ... does not support user-initiated
        // transactions". This is the consumer-side
        // mirror of the OutboxDispatcher.DispatchBatchAsync wrapping.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            foreach (var item in message.Items)
            {
                // 1. Idempotency gate — composite PK (OrderId, MenuItemId) throws on duplicate.
                try
                {
                    dbContext.ProcessedOrderItems.Add(new ProcessedOrderItem
                    {
                        OrderId = message.OrderId,
                        MenuItemId = item.MenuItemId,
                        ProcessedAt = SystemClock.Instance.GetCurrentInstant(),
                    });
                    await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    logger.LogDebug(
                        "OrderCompleted {OrderId} already processed for MenuItem {MenuItemId}; skipping.",
                        message.OrderId, item.MenuItemId);
                    continue;
                }

                // 2. Upsert MenuItemAnalytics row keyed by (MenuItemId, AnalysisDate).
                var row = await dbContext.MenuItemAnalytics
                    .FirstOrDefaultAsync(m =>
                        m.MenuItemId == item.MenuItemId && m.AnalysisDate == analysisDate,
                        ct).ConfigureAwait(false);

                if (row is null)
                {
                    dbContext.MenuItemAnalytics.Add(new MenuItemAnalytics
                    {
                        MenuItemId = item.MenuItemId,
                        RestaurantId = message.RestaurantId,
                        AnalysisDate = analysisDate,
                        TimesOrdered = item.Quantity,
                        TotalRevenue = item.UnitPrice * item.Quantity,
                    });
                }
                else
                {
                    row.TimesOrdered += item.Quantity;
                    row.TotalRevenue += item.UnitPrice * item.Quantity;
                }

                await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }, context.CancellationToken).ConfigureAwait(false);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
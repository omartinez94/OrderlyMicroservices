using System.Security.Claims;
using Assistant.API.Domain.Entities;
using Assistant.API.Infrastructure.Persistence;

namespace Assistant.API.Features.Snapshots;

public sealed class MenuItemChangedSnapshotConsumer(
    IRestaurantContextSnapshotService snapshotService) : IConsumer<MenuItemChangedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<MenuItemChangedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        MenuItemChangedIntegrationEvent msg = context.Message;

        if (msg.RestaurantId == Guid.Empty || string.IsNullOrWhiteSpace(msg.Name))
        {
            return;
        }

        bool isAvailable = msg.ChangeType != MenuItemChangeType.Deleted &&
                           (msg.IsAvailable ?? true) &&
                           !string.Equals(msg.AvailabilityStatus, "Unavailable", StringComparison.OrdinalIgnoreCase);

        await snapshotService.UpdateEightySixedItemAsync(
            msg.RestaurantId,
            msg.Name,
            isAvailable,
            context.CancellationToken).ConfigureAwait(false);
    }
}

public sealed class IngredientAvailabilitySnapshotConsumer(
    IRestaurantContextSnapshotService snapshotService) : IConsumer<IngredientAvailabilityChangedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<IngredientAvailabilityChangedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IngredientAvailabilityChangedIntegrationEvent msg = context.Message;

        if (msg.RestaurantId == Guid.Empty)
        {
            return;
        }

        bool isHealthy = string.Equals(msg.AvailabilityStatus, "Available", StringComparison.OrdinalIgnoreCase);
        await snapshotService.UpdateLowStockIngredientAsync(
            msg.RestaurantId,
            $"Item-{msg.MenuItemId:N}",
            isHealthy,
            context.CancellationToken).ConfigureAwait(false);
    }
}

public sealed class OrderCreatedSnapshotConsumer(
    IRestaurantContextSnapshotService snapshotService) : IConsumer<OrderCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        OrderCreatedIntegrationEvent msg = context.Message;

        if (msg.RestaurantId == Guid.Empty)
        {
            return;
        }

        await snapshotService.RecordOrderActivityAsync(
            msg.RestaurantId,
            activeTicketDelta: 1,
            delayedTicketCount: -1,
            context.CancellationToken).ConfigureAwait(false);
    }
}

public sealed class KitchenOrderBumpedSnapshotConsumer(
    IRestaurantContextSnapshotService snapshotService) : IConsumer<KitchenOrderBumpedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderBumpedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        KitchenOrderBumpedIntegrationEvent msg = context.Message;

        if (msg.RestaurantId == Guid.Empty)
        {
            return;
        }

        await snapshotService.RecordOrderActivityAsync(
            msg.RestaurantId,
            activeTicketDelta: -1,
            delayedTicketCount: -1,
            context.CancellationToken).ConfigureAwait(false);
    }
}

public sealed class TableStatusChangedSnapshotConsumer(
    IRestaurantContextSnapshotService snapshotService) : IConsumer<TableStatusChangedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<TableStatusChangedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TableStatusChangedIntegrationEvent msg = context.Message;

        if (msg.RestaurantId == Guid.Empty)
        {
            return;
        }

        RestaurantOperationalSnapshotDto current = await snapshotService
            .GetSnapshotAsync(msg.RestaurantId, context.CancellationToken)
            .ConfigureAwait(false);

        bool nowOccupied = string.Equals(msg.NewStatus, "Occupied", StringComparison.OrdinalIgnoreCase);
        int occupied = nowOccupied
            ? current.ActiveOccupiedTables + 1
            : Math.Max(0, current.ActiveOccupiedTables - 1);
        int available = nowOccupied
            ? Math.Max(0, current.AvailableTables - 1)
            : current.AvailableTables + 1;

        await snapshotService.UpdateTableOccupancyAsync(
            msg.RestaurantId,
            occupied,
            available,
            context.CancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Idempotent MassTransit consumer that persists <see cref="AiUsageRecordedIntegrationEvent"/>
/// messages into PostgreSQL (<see cref="AiDbContext.UsageLedgers"/>).
/// </summary>
public sealed class AiUsageLedgerConsumer(
    AiDbContext dbContext,
    ICurrentRestaurantProvider restaurantProvider,
    ILogger<AiUsageLedgerConsumer> logger) : IConsumer<AiUsageRecordedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<AiUsageRecordedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        AiUsageRecordedIntegrationEvent msg = context.Message;

        Guid ledgerId = msg.UsageRecordId != Guid.Empty ? msg.UsageRecordId : msg.Id;
        ClaimsPrincipal syntheticPrincipal = new(new ClaimsIdentity(
        [
            new Claim("restaurantId", msg.RestaurantId.ToString())
        ]));

        using IDisposable tenantScope = restaurantProvider.Attach(syntheticPrincipal);

        bool alreadyRecorded = await dbContext.UsageLedgers
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == ledgerId, context.CancellationToken)
            .ConfigureAwait(false);

        if (alreadyRecorded)
        {
            logger.LogInformation("Skipping duplicate AiUsageRecordedIntegrationEvent {LedgerId}", ledgerId);
            return;
        }

        AiUsageLedger ledger = AiUsageLedger.Create(
            id: ledgerId,
            restaurantId: msg.RestaurantId,
            userId: msg.UserId,
            conversationId: msg.ConversationId,
            operationType: msg.OperationType,
            provider: msg.Provider,
            model: msg.Model,
            uncachedPromptTokens: msg.PromptTokens,
            cachedTokens: msg.CachedTokens,
            completionTokens: msg.CompletionTokens,
            estimatedCostUsd: msg.EstimatedCostUsd,
            billingCycle: msg.BillingCycle,
            wasOverage: msg.WasOverage,
            recordedAt: msg.RecordedAt);

        dbContext.UsageLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
    }
}

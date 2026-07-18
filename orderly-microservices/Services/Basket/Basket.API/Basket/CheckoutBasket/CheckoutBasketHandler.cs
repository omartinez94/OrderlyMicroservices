using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Messaging.Events;

namespace Basket.API.Basket.CheckoutBasket;

[PciSensitive]
public record CheckoutBasketCommand(BasketCheckoutDto BasketCheckoutDto) : ICommand<CheckoutBasketResult>, IBasketIdentityRequest
{
    public Guid UserId => BasketCheckoutDto.UserId;
    public Guid RestaurantId => BasketCheckoutDto.RestaurantId;
}

public record CheckoutBasketResult(bool Success, string Message);

public class CheckoutBasketCommandValidator : AbstractValidator<CheckoutBasketCommand>
{
    public CheckoutBasketCommandValidator()
    {
        RuleFor(x => x.BasketCheckoutDto.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.BasketCheckoutDto.RestaurantId).NotEmpty().WithMessage("RestaurantId is required.");
    }
}

/// <summary>
/// Handles <see cref="CheckoutBasketCommand"/> by staging a
/// <c>BasketCheckoutEvent</c> in the outbox table and deleting the
/// cart in the same <see cref="IDocumentSession"/> commit. The
/// background <see cref="CheckoutBasketOutboxDispatcher"/> then relays
/// the staged row onto MassTransit — the publish itself no longer
/// races against the delete.
/// </summary>
/// <remarks>
/// <para>
/// <b>Single-responsibility exception</b> (per plan §0.3.7): the handler
/// intentionally performs six steps (load → validate → build event →
/// stage outbox → delete cart → commit) so the publish-and-delete
/// atomicity holds. The exception is deliberate and recorded in the
/// commit message — verify reviewers see the rationale, not the
/// omission.
/// </para>
/// <para>
/// <b>IDocumentSession is scoped</b> (Marten <c>UseLightweightSessions()</c>);
/// the same instance flows through <see cref="IBasketRepository"/>
/// for the read and into this handler for the writes. One
/// <c>SaveChangesAsync</c> commits the outbox row + the Basket delete
/// atomically.
/// </para>
/// <para>
/// <b>Card-redaction on the wire</b> is deferred to a Phase 2.1 commit
/// (separate BuildingBlocks contribution — <c>BasketCheckoutEvent</c>
/// lives in <c>BuildingBlocks.Messaging.Events</c>). This commit
/// preserves the existing wire shape so Ordering's consumer doesn't
/// break; the redacted <c>PaymentMethodSummary</c> shape lands in the
/// follow-up.
/// </para>
/// </remarks>
public class CheckoutBasketCommandHandler(
    IBasketRepository basketRepository,
    IDocumentSession session,
    ILogger<CheckoutBasketCommandHandler> logger)
    : ICommandHandler<CheckoutBasketCommand, CheckoutBasketResult>
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<CheckoutBasketResult> Handle(CheckoutBasketCommand command, CancellationToken cancellationToken)
    {
        var basket = await basketRepository.GetBasketAsync(
            command.BasketCheckoutDto.UserId,
            command.BasketCheckoutDto.RestaurantId,
            cancellationToken);

        if (basket.Items.Count == 0)
        {
            return new CheckoutBasketResult(false, "Basket is empty.");
        }

        var eventMessage = command.BasketCheckoutDto.Adapt<BasketCheckoutEvent>() with
        {
            TotalAmount = basket.Subtotal,
            AppliedDiscounts = basket.AppliedDiscounts,
            Items = [.. basket.Items.Select(item => new BasketCheckoutItem
            {
                MenuItemId = item.MenuItemId,
                Name = string.Empty,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.TotalPrice,
                Variations = [.. item.Variations.Select(v => new BasketItemVariationDto
                {
                    Name = v.Name,
                    Value = v.Value,
                    Price = v.Price
                })],
                Customizations = [.. item.Customizations.Select(c => new BasketItemCustomizationDto
                {
                    Ingredient = c.Ingredient,
                    Action = c.Action
                })]
            })]
        };

        // Stage outbox row + delete basket in one Marten session so the
        // commit either lands both or lands neither. The dispatcher
        // (CheckoutBasketOutboxDispatcher) is the only thing that calls
        // IPublishEndpoint.Publish from here on.
        var outboxMessage = new CheckoutBasketOutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOn = SystemClock.Instance.GetCurrentInstant(),
            Type = typeof(BasketCheckoutEvent).FullName!,
            Payload = JsonSerializer.Serialize(eventMessage, SerializerOptions),
            SchemaVersion = 1,
        };

        session.Store(outboxMessage);
        session.Delete(basket);
        await session.SaveChangesAsync(cancellationToken);

        // Cache invalidation runs AFTER the Marten commit so a
        // concurrent reader on the same cache key can't see a
        // basket that's already deleted. Cache TTL (30 min) bounds
        // the staleness window if the Redis call itself fails —
        // tracked as a Phase 4 /live + /ready concern.
        await basketRepository.InvalidateCacheAsync(
            command.BasketCheckoutDto.UserId,
            command.BasketCheckoutDto.RestaurantId,
            cancellationToken);

        logger.LogInformation(
            "Basket checkout staged. Outbox row {OutboxMessageId} for ({UserId}, {RestaurantId}) — {ItemCount} item(s), total {Total}.",
            outboxMessage.Id,
            basket.UserId,
            basket.RestaurantId,
            basket.Items.Count,
            basket.Subtotal);

        return new CheckoutBasketResult(true, "Checkout completed successfully.");
    }
}

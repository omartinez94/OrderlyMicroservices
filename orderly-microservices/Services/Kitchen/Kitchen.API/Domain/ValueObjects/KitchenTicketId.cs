namespace Kitchen.API.Domain.ValueObjects;

/// <summary>
/// Typed identifier for a <see cref="Aggregates.KitchenTicket.KitchenTicket"/>.
/// Reuses the originating <c>Order.Id</c> so that the kitchen-side projection
/// correlates 1:1 with the upstream order — there is no separate ticket sequence.
/// </summary>
public record KitchenTicketId
{
    public Guid Value { get; }
    private KitchenTicketId(Guid value) => Value = value;

    public static KitchenTicketId Of(Guid value)
    {
        if (value == Guid.Empty)
            throw new KitchenDomainException("KitchenTicketId cannot be empty.", nameof(value));

        return new(value);
    }

    public static KitchenTicketId FromOrderId(Guid orderId) => Of(orderId);
}
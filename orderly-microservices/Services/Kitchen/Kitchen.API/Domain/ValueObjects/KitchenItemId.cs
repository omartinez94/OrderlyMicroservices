namespace Kitchen.API.Domain.ValueObjects;

/// <summary>
/// Typed identifier for a <see cref="Aggregates.KitchenTicket.KitchenTicketItem"/>.
/// Reuses the originating <c>OrderItem.Id</c> for 1:1 correlation with the
/// upstream order's line items.
/// </summary>
public record KitchenItemId
{
    public Guid Value { get; }
    private KitchenItemId(Guid value) => Value = value;

    public static KitchenItemId Of(Guid value)
    {
        if (value == Guid.Empty)
            throw new KitchenDomainException("KitchenItemId cannot be empty.", nameof(value));

        return new(value);
    }
}
namespace Ordering.Domain.ValueObjects;

/// <summary>
/// Strongly-typed identifier for an <see cref="Models.OrderActivity"/> row.
/// Mirrors the <see cref="OrderId"/> / <see cref="OrderItemId"/> convention.
/// </summary>
public record OrderActivityId
{
    public Guid Value { get; }
    private OrderActivityId(Guid value) => Value = value;

    public static OrderActivityId Of(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("OrderActivityId cannot be empty.", nameof(value));

        return new(value);
    }
}
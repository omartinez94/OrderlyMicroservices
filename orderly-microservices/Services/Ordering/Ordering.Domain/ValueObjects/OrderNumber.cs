namespace Ordering.Domain.ValueObjects;

public record OrderNumber : IComparable<OrderNumber>
{
    public string Value { get; }
    private OrderNumber(string value) => Value = value;
    public static OrderNumber Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("OrderNumber cannot be empty.", nameof(value));
        }

        return new(value);
    }

    /// <summary>
    /// Lexical compare on the underlying string. Required for in-memory
    /// LINQ <c>OrderBy(o =&gt; o.OrderNumber)</c> in the unit tests' mock
    /// provider (EF Core translates to SQL ORDER BY in production).
    /// </summary>
    public int CompareTo(OrderNumber? other)
        => string.Compare(Value, other?.Value, StringComparison.Ordinal);
}

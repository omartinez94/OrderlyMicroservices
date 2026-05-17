namespace Ordering.Domain.ValueObjects;

public record OrderNumber
{
    public string Value { get; }
    private OrderNumber(string value) => Value = value;
    public static OrderNumber Of(string value)
    {
        if(string.IsNullOrWhiteSpace(value))
            throw new DomainException("OrderNumber cannot be empty.", nameof(value));

        return new(value);
    }
}

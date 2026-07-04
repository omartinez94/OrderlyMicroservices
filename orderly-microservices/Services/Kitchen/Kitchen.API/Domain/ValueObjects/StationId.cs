namespace Kitchen.API.Domain.ValueObjects;

/// <summary>
/// Typed identifier for a <see cref="Aggregates.KitchenStation.KitchenStation"/>.
/// </summary>
public record StationId
{
    public Guid Value { get; }
    private StationId(Guid value) => Value = value;

    public static StationId Of(Guid value)
    {
        if (value == Guid.Empty)
            throw new KitchenDomainException("StationId cannot be empty.", nameof(value));

        return new(value);
    }
}
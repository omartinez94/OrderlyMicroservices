using System.Globalization;
using System.Text.RegularExpressions;
using BuildingBlocks.Enums;
using NodaTime;

namespace Ordering.Domain.ValueObjects;

public partial record OrderNumber : IComparable<OrderNumber>
{
    public string Value { get; }
    public string? Channel { get; }
    public LocalDate? Date { get; }
    public int? SequenceNumber { get; }

    public string DisplayTicket => Channel is not null && SequenceNumber.HasValue
        ? $"{Channel} #{SequenceNumber.Value}"
        : Value;

    private OrderNumber(string value, string? channel = null, LocalDate? date = null, int? sequenceNumber = null)
    {
        Value = value;
        Channel = channel;
        Date = date;
        SequenceNumber = sequenceNumber;
    }

    public static OrderNumber Create(string channel, LocalDate date, int sequence)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new DomainException("Channel cannot be empty.", nameof(channel));
        }

        string normalizedChannel = channel.Trim().ToUpperInvariant();
        if (normalizedChannel.Length != 3)
        {
            throw new DomainException("Channel code must be exactly 3 characters.", nameof(channel));
        }

        if (sequence <= 0)
        {
            throw new DomainException("Sequence number must be greater than zero.", nameof(sequence));
        }

        string formattedValue = $"{normalizedChannel}{date.Year:D4}{date.Month:D2}{date.Day:D2}-{sequence:D4}";
        return new OrderNumber(formattedValue, normalizedChannel, date, sequence);
    }

    public static OrderNumber Create(OrderType orderType, LocalDate date, int sequence) =>
        Create(ToChannelCode(orderType), date, sequence);

    public static string ToChannelCode(OrderType orderType) => orderType switch
    {
        OrderType.DineIn => "DIN",
        OrderType.Takeout => "TAK",
        OrderType.Delivery => "DEL",
        _ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unknown order type.")
    };

    public static OrderNumber Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("OrderNumber cannot be empty.", nameof(value));
        }

        Match match = OrderNumberRegex().Match(value.Trim());
        if (match.Success &&
            int.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out int year) &&
            int.TryParse(match.Groups[3].Value, CultureInfo.InvariantCulture, out int month) &&
            int.TryParse(match.Groups[4].Value, CultureInfo.InvariantCulture, out int day) &&
            int.TryParse(match.Groups[5].Value, CultureInfo.InvariantCulture, out int sequence))
        {
            try
            {
                LocalDate date = new(year, month, day);
                string channel = match.Groups[1].Value.ToUpperInvariant();
                return new OrderNumber(value, channel, date, sequence);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid calendar date, treat as non-standard/legacy
            }
        }

        return new OrderNumber(value);
    }

    /// <summary>
    /// Lexical compare on the underlying string. Required for in-memory
    /// LINQ <c>OrderBy(o =&gt; o.OrderNumber)</c> in the unit tests' mock
    /// provider (EF Core translates to SQL ORDER BY in production).
    /// </summary>
    public int CompareTo(OrderNumber? other)
        => string.Compare(Value, other?.Value, StringComparison.Ordinal);

    public override string ToString() => Value;

    [GeneratedRegex(@"\A([A-Za-z]{3})(\d{4})(\d{2})(\d{2})-(\d+)\z")]
    private static partial Regex OrderNumberRegex();
}

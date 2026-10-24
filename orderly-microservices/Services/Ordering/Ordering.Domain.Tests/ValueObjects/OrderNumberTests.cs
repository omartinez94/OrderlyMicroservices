using BuildingBlocks.Enums;
using NodaTime;

namespace Ordering.Domain.Tests.ValueObjects;

/// <summary>
/// Covers the factory and formatting rules for <see cref="OrderNumber"/>:
/// standard daily sequence format {Channel}{yyyyMMdd}-{xxxx}, legacy format fallback,
/// channel code mapping, and DisplayTicket helper.
/// </summary>
public sealed class OrderNumberTests
{
    [Fact]
    public void Create_WithValidChannelDateAndSequence_FormatsCorrectly()
    {
        LocalDate date = new(2026, 10, 23);
        OrderNumber orderNumber = OrderNumber.Create("DIN", date, 1);

        orderNumber.Value.Should().Be("DIN20261023-0001");
        orderNumber.Channel.Should().Be("DIN");
        orderNumber.Date.Should().Be(date);
        orderNumber.SequenceNumber.Should().Be(1);
        orderNumber.DisplayTicket.Should().Be("DIN #1");
    }

    [Theory]
    [InlineData(OrderType.DineIn, "DIN", 1, "DIN20261023-0001", "DIN #1")]
    [InlineData(OrderType.Takeout, "TAK", 2, "TAK20261023-0002", "TAK #2")]
    [InlineData(OrderType.Delivery, "DEL", 123, "DEL20261023-0123", "DEL #123")]
    public void Create_WithOrderType_MapsChannelAndFormatsCorrectly(
        OrderType orderType,
        string expectedChannel,
        int sequence,
        string expectedValue,
        string expectedDisplayTicket)
    {
        LocalDate date = new(2026, 10, 23);
        OrderNumber orderNumber = OrderNumber.Create(orderType, date, sequence);

        orderNumber.Value.Should().Be(expectedValue);
        orderNumber.Channel.Should().Be(expectedChannel);
        orderNumber.Date.Should().Be(date);
        orderNumber.SequenceNumber.Should().Be(sequence);
        orderNumber.DisplayTicket.Should().Be(expectedDisplayTicket);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithZeroOrNegativeSequence_ThrowsDomainException(int sequence)
    {
        LocalDate date = new(2026, 10, 23);
        Action act = () => OrderNumber.Create("DIN", date, sequence);

        act.Should().Throw<DomainException>()
            .WithMessage("*Sequence number must be greater than zero.*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyChannel_ThrowsDomainException(string? channel)
    {
        LocalDate date = new(2026, 10, 23);
        Action act = () => OrderNumber.Create(channel!, date, 1);

        act.Should().Throw<DomainException>()
            .WithMessage("*Channel cannot be empty.*");
    }

    [Theory]
    [InlineData("DI")]
    [InlineData("DINE")]
    public void Create_WithInvalidChannelLength_ThrowsDomainException(string channel)
    {
        LocalDate date = new(2026, 10, 23);
        Action act = () => OrderNumber.Create(channel, date, 1);

        act.Should().Throw<DomainException>()
            .WithMessage("*Channel code must be exactly 3 characters.*");
    }

    [Fact]
    public void Of_WithStandardFormattedValue_ParsesComponentsAndDisplayTicket()
    {
        OrderNumber orderNumber = OrderNumber.Of("TAK20261023-0042");

        orderNumber.Value.Should().Be("TAK20261023-0042");
        orderNumber.Channel.Should().Be("TAK");
        orderNumber.Date.Should().Be(new LocalDate(2026, 10, 23));
        orderNumber.SequenceNumber.Should().Be(42);
        orderNumber.DisplayTicket.Should().Be("TAK #42");
    }

    [Fact]
    public void Of_WithLowercaseFormattedValue_NormalizesChannelAndDisplayTicket()
    {
        OrderNumber orderNumber = OrderNumber.Of("del20261023-0007");

        orderNumber.Channel.Should().Be("DEL");
        orderNumber.Date.Should().Be(new LocalDate(2026, 10, 23));
        orderNumber.SequenceNumber.Should().Be(7);
        orderNumber.DisplayTicket.Should().Be("DEL #7");
    }

    [Fact]
    public void Of_WithLegacyValue_LeavesComponentsNullAndReturnsValueAsDisplayTicket()
    {
        OrderNumber orderNumber = OrderNumber.Of("ORD-2026-0001");

        orderNumber.Value.Should().Be("ORD-2026-0001");
        orderNumber.Channel.Should().BeNull();
        orderNumber.Date.Should().BeNull();
        orderNumber.SequenceNumber.Should().BeNull();
        orderNumber.DisplayTicket.Should().Be("ORD-2026-0001");
    }

    [Fact]
    public void Of_WithNull_Throws()
    {
        Action act = () => OrderNumber.Of(null!);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: OrderNumber cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }

    [Fact]
    public void Of_WithEmpty_Throws()
    {
        Action act = () => OrderNumber.Of(string.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: OrderNumber cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }

    [Fact]
    public void Of_WithWhitespace_Throws()
    {
        Action act = () => OrderNumber.Of("   ");

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: OrderNumber cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }
}

namespace Ordering.Domain.Tests.ValueObjects;

/// <summary>
/// Covers every guard rule of <see cref="Address.Of(string, string, string, string, string)"/>.
/// Each negative test exists to lock in one specific invariant so a future refactor that
/// drops or weakens a guard fails loudly instead of silently accepting bad data.
/// </summary>
public sealed class AddressTests
{
    private const string Street = "123 Main St";
    private const string City = "Springfield";
    private const string State = "IL";
    private const string ZipCode = "12345";
    private const string Country = "US";

    /// <summary>
    /// Happy path: a fully-populated <see cref="Address"/> round-trips all five fields
    /// unchanged. Guards against properties being silently re-mapped to the wrong source.
    /// </summary>
    [Fact]
    public void Of_WithAllValidFields_ReturnsAddressWithSameValues()
    {
        var address = Address.Of(Street, City, State, ZipCode, Country);

        address.Street.Should().Be(Street);
        address.City.Should().Be(City);
        address.State.Should().Be(State);
        address.ZipCode.Should().Be(ZipCode);
        address.Country.Should().Be(Country);
    }

    /// <summary>
    /// Verifies the "Street cannot be empty" guard: null, empty, and whitespace-only
    /// strings must all reject. This is the first line of defense against addresses
    /// with missing street data leaking into orders and bills.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyStreet_Throws(string? street)
    {
        Action act = () => Address.Of(street, City, State, ZipCode, Country);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: Street cannot be empty. throws from Domain Layer. (Parameter: street)*");
    }

    /// <summary>
    /// Verifies the "City cannot be empty" guard. Mirrors the street test so every
    /// non-zipcode string field has identical treatment.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyCity_Throws(string? city)
    {
        Action act = () => Address.Of(Street, city, State, ZipCode, Country);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: City cannot be empty. throws from Domain Layer. (Parameter: city)*");
    }

    /// <summary>
    /// Verifies the "State cannot be empty" guard.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyState_Throws(string? state)
    {
        Action act = () => Address.Of(Street, City, state, ZipCode, Country);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: State cannot be empty. throws from Domain Layer. (Parameter: state)*");
    }

    /// <summary>
    /// Verifies the "ZipCode cannot be empty" guard — null/empty/whitespace all reject
    /// before any length check.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyZipCode_Throws(string? zipCode)
    {
        Action act = () => Address.Of(Street, City, State, zipCode, Country);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: ZipCode cannot be empty. throws from Domain Layer. (Parameter: zipCode)*");
    }

    /// <summary>
    /// Verifies the "ZipCode must be 5 characters" length guard. Covers both too-short
    /// and too-long inputs (including ZIP+4 format which the current code does not accept).
    /// If/when ZIP+4 support is added this test should be updated to assert the
    /// accepted format rather than the throw.
    /// </summary>
    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    [InlineData("123456")]
    [InlineData("12345-6789")]
    public void Of_WithZipCodeNotFiveChars_Throws(string zipCode)
    {
        Action act = () => Address.Of(Street, City, State, zipCode, Country);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: ZipCode must be 5 characters. throws from Domain Layer. (Parameter: zipCode)*");
    }

    /// <summary>
    /// Verifies the "Country cannot be empty" guard.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyCountry_Throws(string? country)
    {
        Action act = () => Address.Of(Street, City, State, ZipCode, country);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: Country cannot be empty. throws from Domain Layer. (Parameter: country)*");
    }
}
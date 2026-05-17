namespace Ordering.Domain.ValueObjects;

public record Address
{
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string ZipCode { get; }
    public string Country { get; }

    protected Address() { }

    private Address(string street, string city, string state, string zipCode, string country)
    {
        Street = street;
        City = city;
        State = state;
        ZipCode = zipCode;
        Country = country;
    }

    public static Address Of(string street, string city, string state, string zipCode, string country)
    {
        if (string.IsNullOrWhiteSpace(street))
            throw new DomainException("Street cannot be empty.", nameof(street));
        if (string.IsNullOrWhiteSpace(city))
            throw new DomainException("City cannot be empty.", nameof(city));
        if (string.IsNullOrWhiteSpace(state))
            throw new DomainException("State cannot be empty.", nameof(state));
        if (string.IsNullOrWhiteSpace(zipCode))
            throw new DomainException("ZipCode cannot be empty.", nameof(zipCode));
        if (zipCode.Length != 5)
            throw new DomainException("ZipCode must be 5 characters.", nameof(zipCode));
        if (string.IsNullOrWhiteSpace(country))
            throw new DomainException("Country cannot be empty.", nameof(country));

        return new(street, city, state, zipCode, country);
    }
}

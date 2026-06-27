namespace Ordering.Domain.Tests.Models;

/// <summary>
/// Covers <see cref="Customer.Create"/>. The factory enforces two hard invariants
/// (email and name must be non-empty) and one soft contract (address is optional).
/// </summary>
public sealed class CustomerTests
{
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    /// <summary>
    /// Happy path: every field — including the optional address — round-trips through
    /// the factory unchanged.
    /// </summary>
    [Fact]
    public void Create_WithValidArgs_ReturnsCustomerWithIdAndFields()
    {
        var id = NewCustomerId();
        const string email = "john@example.com";
        const string name = "John Doe";
        const string phone = "555-1234";
        var address = ValidAddress();

        var customer = Customer.Create(id, email, name, phone, address);

        customer.Id.Should().Be(id);
        customer.Email.Should().Be(email);
        customer.Name.Should().Be(name);
        customer.Phone.Should().Be(phone);
        customer.Address.Should().BeSameAs(address);
    }

    /// <summary>
    /// Email guard: null, empty, and whitespace all reject. <see cref="ArgumentException"/>
    /// (or its <see cref="ArgumentNullException"/> subtype for null) is thrown with the
    /// parameter name identifying the offending field.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespaceEmail_Throws(string? email)
    {
        Action act = () => Customer.Create(NewCustomerId(), email!, "John Doe", "555-1234");

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    /// <summary>
    /// Name guard: null, empty, and whitespace all reject — same pattern as the email guard.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespaceName_Throws(string? name)
    {
        Action act = () => Customer.Create(NewCustomerId(), "john@example.com", name!, "555-1234");

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    /// <summary>
    /// Documents that a null address is allowed: the customer can be created without
    /// an address on file (address is added later, typically when the customer places
    /// their first delivery order).
    /// </summary>
    [Fact]
    public void Create_WithNullAddress_IsAllowed()
    {
        var customer = Customer.Create(NewCustomerId(), "john@example.com", "John Doe", "555-1234", address: null);

        customer.Address.Should().BeNull();
    }
}
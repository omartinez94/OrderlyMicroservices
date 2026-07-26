using BuildingBlocks.Messaging.Events;

namespace Basket.API.Tests.Integration;

/// <summary>
/// Shared seeding helpers for the Basket integration tests. Inserts go
/// through a real <see cref="IDocumentSession"/> resolved from the
/// factory's scope so the production
/// <see cref="BuildingBlocks.Multitenancy.ClaimsRestaurantProvider"/>
/// applies the same <c>MultiTenanted()</c> filter the production code
/// path uses.
/// </summary>
/// <remarks>
/// <para>The seed resolves the current tenant from the
/// <c>restaurantId</c> claim on the WAF's
/// <see cref="TestAuthHandler.TestRestaurantId"/>; the basket is then
/// stored with that restaurant id so the
/// <see cref="IBasketRepository.AssertTenant"/> check passes for
/// subsequent endpoint calls. Mirrors the shape of
/// <c>Catalog.API.Tests.Integration.SeedHelper</c>.</para>
/// <para>The session bypasses the <c>IBasketRepository</c> decorator
/// chain (which would assert tenant + write-through Redis); for tests
/// we want the raw Marten write so the cache layer is not warmed
/// before the endpoint test runs.</para>
/// </remarks>
internal static class BasketSeedHelper
{
    /// <summary>
    /// Stable restaurant id for the entire test collection. Matches
    /// <see cref="TestAuthHandler.TestRestaurantId"/> so the
    /// production tenant filter is satisfied for every endpoint call.
    /// </summary>
    public static readonly Guid TestRestaurantId =
        Guid.Parse(TestAuthHandler.TestRestaurantId);

    /// <summary>
    /// Inserts a minimal valid <see cref="Models.Basket"/> for the given
    /// <paramref name="userId"/> under the test tenant and returns
    /// the persisted document. The basket is empty (no items, no
    /// coupons) by default; callers configure the rest via
    /// <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// Opens a tenant-scoped <see cref="IDocumentSession"/> via
    /// <c>IDocumentStore.LightweightSession(tenantId)</c> rather than
    /// the default scoped <see cref="IDocumentSession"/>. The reason:
    /// the scoped session inherits its <c>TenantId</c> from the
    /// ambient <c>SessionOptions</c>, which the
    /// <c>ClaimsRestaurantProvider</c> only sets inside an HTTP
    /// request scope. Outside a request (this helper runs in the
    /// test process, not in an HTTP request) the provider returns
    /// <see cref="Guid.Empty"/> and the global
    /// <c>MultiTenanted()</c> filter writes the row to the
    /// <c>*DEFAULT*</c> tenant — not the test restaurant — and the
    /// endpoint test that reads the basket as the test user sees
    /// nothing. The explicit
    /// <c>IDocumentStore.LightweightSession(TestRestaurantId.ToString())</c>
    /// forces the row into the test tenant regardless of ambient
    /// provider state.
    /// </remarks>
    public static async Task<Models.Basket> SeedBasketAsync(
        this BasketWebApplicationFactory factory,
        Guid userId,
        Action<Models.Basket>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var store = factory.Services.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession(TestRestaurantId.ToString());

        var now = SystemClock.Instance.GetCurrentInstant();
        var basket = new Models.Basket
        {
            UserId = userId,
            RestaurantId = TestRestaurantId,
            CreatedAt = now,
            ExpiresAt = now + Duration.FromMinutes(30),
            LastModifiedAt = now,
        };
        configure?.Invoke(basket);

        session.Store(basket);
        await session.SaveChangesAsync();
        return basket;
    }

    /// <summary>
    /// Inserts a basket whose <see cref="Models.Basket.ExpiresAt"/> is already
    /// in the past, for the <c>BasketExpirySweepTests</c>. The
    /// <see cref="Models.Basket.LastModifiedAt"/> is set to the same
    /// already-past instant so the document sort order is stable
    /// across runs.
    /// </summary>
    public static async Task<Models.Basket> SeedExpiredBasketAsync(
        this BasketWebApplicationFactory factory,
        Guid userId,
        Duration? age = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var now = SystemClock.Instance.GetCurrentInstant();
        var past = now - (age ?? Duration.FromHours(1));

        return await factory.SeedBasketAsync(userId, b =>
        {
            b.CreatedAt = past - Duration.FromMinutes(30);
            b.ExpiresAt = past;
            b.LastModifiedAt = past;
        });
    }

    /// <summary>
    /// Convenience for tests that need a fully-valid
    /// <see cref="BasketCheckoutDto"/> body. The DTO is what the
    /// <c>POST /api/v1/cart/checkout</c> route binds to; the
    /// <c>UserId</c> / <c>RestaurantId</c> properties are set to
    /// <see cref="Guid.Empty"/> because the endpoint overwrites them
    /// with the JWT-derived values before constructing the command
    /// (the §0.4.10 spoofing-footgun fix).
    /// </summary>
    public static BasketCheckoutDto BuildValidCheckoutDto(
        Guid userId = default,
        Guid restaurantId = default)
    {
        return new BasketCheckoutDto
        {
            UserId = userId == default ? Guid.Empty : userId,
            RestaurantId = restaurantId == default ? Guid.Empty : restaurantId,
            FirstName = "Test",
            LastName = "User",
            EmailAddress = "test.user@basket.local",
            AddressLine = "123 Test Street",
            Country = "US",
            State = "CA",
            ZipCode = "94000",
            CardName = "Test User",
            CardNumber = "4242424242424242", // Luhn-valid Visa test PAN
            CVV = "123",
            Expiration = "12/30",
            PaymentMethod = PaymentMethod.Card,
        };
    }
}

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// xUnit collection definition ensuring the
/// <see cref="DiscountWebApplicationFactory"/> (a Testcontainers-managed
/// PostgreSQL host) is created once for all integration tests under this
/// collection. Decorate each integration test class with
/// <c>[Collection(nameof(DiscountWebApplicationFactoryCollection))]</c>.
/// </summary>
[CollectionDefinition(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class DiscountWebApplicationFactoryCollection
    : ICollectionFixture<DiscountWebApplicationFactory>
{
}

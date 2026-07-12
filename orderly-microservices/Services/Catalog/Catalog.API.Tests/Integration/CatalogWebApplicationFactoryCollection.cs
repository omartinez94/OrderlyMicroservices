namespace Catalog.API.Tests.Integration;

/// <summary>
/// xUnit collection definition ensuring the
/// <see cref="CatalogWebApplicationFactory"/> (Postgres + Redis + RabbitMQ
/// containers) is created once for all integration tests. Decorate each
/// integration test class with
/// <c>[Collection(nameof(CatalogWebApplicationFactoryCollection))]</c>.
/// </summary>
[CollectionDefinition(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class CatalogWebApplicationFactoryCollection
    : ICollectionFixture<CatalogWebApplicationFactory>
{
}

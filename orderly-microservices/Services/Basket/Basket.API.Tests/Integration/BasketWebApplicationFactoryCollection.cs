namespace Basket.API.Tests.Integration;

/// <summary>
/// xUnit collection definition ensuring the
/// <see cref="BasketWebApplicationFactory"/> (Postgres + Redis +
/// RabbitMQ containers) is created once for all integration tests.
/// Decorate each integration test class with
/// <c>[Collection(nameof(BasketWebApplicationFactoryCollection))]</c>.
/// </summary>
[CollectionDefinition(nameof(BasketWebApplicationFactoryCollection))]
public sealed class BasketWebApplicationFactoryCollection
    : ICollectionFixture<BasketWebApplicationFactory>
{
}

/// <summary>
/// xUnit collection definition for the expiry-sweep integration
/// tests. Uses a separate factory that re-enables the
/// <c>BasketExpirySweepService</c> hosted service. The factory
/// owns its own Postgres + Redis + RabbitMQ containers
/// (Testcontainers reuses the cached images but each fixture
/// instance has its own container state).
/// </summary>
[CollectionDefinition(nameof(BasketExpirySweepWebApplicationFactoryCollection))]
public sealed class BasketExpirySweepWebApplicationFactoryCollection
    : ICollectionFixture<BasketExpirySweepWebApplicationFactory>
{
}

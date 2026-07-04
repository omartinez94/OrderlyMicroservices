namespace Kitchen.API.Tests.Integration;

/// <summary>
/// xUnit collection definition that ensures the
/// <see cref="KitchenWebApplicationFactory"/> is created once per test run
/// (Postgres + RabbitMQ containers spin up together). All integration tests
/// inherit this collection by decorating their class with
/// <c>[Collection(nameof(KitchenWebApplicationFactoryCollection))]</c>.
/// </summary>
[CollectionDefinition(nameof(KitchenWebApplicationFactoryCollection))]
public sealed class KitchenWebApplicationFactoryCollection
    : ICollectionFixture<KitchenWebApplicationFactory>
{
}
namespace Ordering.API.Tests.Integration;

/// <summary>
/// xUnit collection definition that ensures the
/// <see cref="OrderingWebApplicationFactory"/> is created once per test
/// run (MSSQL + RabbitMQ containers spin up together). All integration
/// tests inherit this collection by decorating their class with
/// <c>[Collection(nameof(OrderingWebApplicationFactoryCollection))]</c>.
/// </summary>
[CollectionDefinition(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderingWebApplicationFactoryCollection
    : ICollectionFixture<OrderingWebApplicationFactory>
{
}

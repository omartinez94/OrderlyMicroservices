namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Drop-in replacement for <c>MassTransit.IPublishEndpoint</c> used by
/// domain-event handlers that want at-least-once delivery semantics.
/// Instead of pushing straight onto the broker, <see cref="PublishAsync{T}"/>
/// stages the message in the same EF Core transaction as the aggregate
/// mutation that produced it. The <c>OutboxDispatcher</c> IHostedService
/// then relays staged rows onto the broker in the background.
///
/// Consumers are responsible for idempotency — use
/// <c>IntegrationEvent.Id</c> (constructor-set in M0) as the dedup key.
/// </summary>
public interface IOutboxPublisher
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class;
}
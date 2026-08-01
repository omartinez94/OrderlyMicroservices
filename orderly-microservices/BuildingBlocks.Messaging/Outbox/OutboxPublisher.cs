using System.Text.Json.Serialization;
using BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Logging;
using NodaTime.Serialization.SystemTextJson;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Stages an integration event in the outbox table inside the same EF Core
/// transaction as the aggregate mutation. The stage happens via the same
/// <see cref="IOutboxDbContext"/> the caller is already mutating, so the
/// broker relay becomes "at-least-once" — a process crash between commit
/// and broker publish can no longer lose the event because the
/// <see cref="OutboxDispatcher"/> picks up the row on restart.
///
/// Replace <c>IPublishEndpoint.Publish</c> with <c>IOutboxPublisher.PublishAsync</c>
/// in domain-event handlers. The two share an identical signature so the
/// call site change is mechanical.
/// </summary>
public abstract class OutboxPublisher<TContext> : IOutboxPublisher
    where TContext : class, IOutboxDbContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    }.ConfigureForNodaTime(NodaTime.DateTimeZoneProviders.Tzdb);

    /// <summary>The ambient DbContext whose change tracker receives the
    /// outbox row. Resolved per-call so the publisher respects the
    /// ambient scope (the same scope as the originating aggregate
    /// mutation).</summary>
    protected abstract TContext ResolveContext();

    protected virtual ILogger Logger => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public virtual async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.Serialize(message, SerializerOptions);
        var schemaVersion = (message as IntegrationEvent)?.MessageVersion ?? 1;
        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOn = SystemClock.Instance.GetCurrentInstant(),
            Type = typeof(T).AssemblyQualifiedName!,
            Payload = payload,
            DispatchedAt = null,
            SchemaVersion = schemaVersion,
        };

        await ResolveContext().OutboxMessages.AddAsync(row, cancellationToken);
        Logger.LogDebug(
            "Outbox row {OutboxId} staged for type {MessageType} (schema v{Schema})",
            row.Id,
            row.Type,
            schemaVersion);
    }
}
using System.ComponentModel.DataAnnotations;

namespace Discount.Grpc.Models;

/// <summary>
/// Consumer-side idempotency log. Plan §0.3.4 commits the project to
/// "if the handler's own side-effect can be made unique-key-deterministic,
/// use that. Otherwise, gate on processed_inbound_events." For
/// <see cref="DiscountRule"/> re-evaluation the rule-update path has no
/// natural uniqueness violation to lean on, so the table guard is
/// mandatory. Composite PK on <c>(EventId, ConsumerType)</c> ensures
/// a redelivery against the same event fails fast with a unique-key
/// violation that the handler swallows.
/// </summary>
public class ProcessedInboundevent
{
    /// <summary>The bus event's <see cref="BuildingBlocks.Messaging.Events.IntegrationEvent.Id"/>
    /// (a Guid stamped at publish time).</summary>
    public Guid EventId { get; set; }

    /// <summary>Discriminator — name of the consumer type that processed
    /// the row (e.g., <c>"MenuItemChangedConsumer"</c>). Different
    /// consumers of the same event land in separate rows.</summary>
    [MaxLength(200)]
    public string ConsumerType { get; set; } = default!;

    /// <summary>UTC wall-clock moment the row was inserted. Diagnostic
    /// only — not part of the dedup contract.</summary>
    public DateTime ConsumedAt { get; set; } = DateTime.UtcNow;
}

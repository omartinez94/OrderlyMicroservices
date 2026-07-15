using BuildingBlocks.Messaging.Events;

namespace Discount.Grpc.Messaging.Events;

/// <summary>
/// Published by Discount.Grpc on every <c>Coupon</c>, <c>RewardCode</c>,
/// and <c>DiscountRule</c> CUD + redeem. Catalog consumes the event and
/// writes a Marten <c>EntityHistoryArchive</c> document (per plan §6.6.1).
/// </summary>
/// <remarks>
/// <para><b>Field shape (locked by plan §6.5):</b></para>
/// <list type="bullet">
/// <item><see cref="EntityType"/> — <c>"Coupon"</c> | <c>"RewardCode"</c> | <c>"DiscountRule"</c>.</item>
/// <item><see cref="EntityId"/> — the aggregate's PK.</item>
/// <item><see cref="RestaurantId"/> — tenant scope.</item>
/// <item><see cref="ChangeType"/> — <c>"Created"</c> | <c>"Updated"</c> | <c>"Deleted"</c> | <c>"Redeemed"</c>.</item>
/// <item><see cref="OldValues"/> — nullable string of serialized JSON;
/// <see langword="null"/> for <c>Created</c>. Serialized via
/// <c>JsonSerializer.Serialize(protoModel)</c> on the publisher side.</item>
/// <item><see cref="NewValues"/> — string of serialized JSON of the
/// post-mutation proto model.</item>
/// </list>
/// <para><b>Why strings, not <c>JsonObject</c>:</b> the wire format is
/// <c>string?</c> (plan v1.1 M9) — every publisher-to-outbox roundtrip
/// would pay an unnecessary serialize-parse tax if we shipped
/// <c>JsonObject</c> on the bus. Catalog parses back via
/// <c>JsonNode.Parse(evt.OldValues)</c> on the consumer side.</para>
/// <para><b>Base-class fields:</b> <see cref="IntegrationEvent.Id"/>,
/// <see cref="IntegrationEvent.OccurredOn"/>, and
/// <see cref="IntegrationEvent.MessageVersion"/> are inherited from the
/// <see cref="IntegrationEvent"/> base. Do NOT redeclare them on the
/// record (shadowing causes MassTransit serialization confusion per plan
/// v1.1 H3).</para>
/// <para><b>Correlation flow:</b> the <c>CorrelationId</c> is not a record
/// field — it flows via the MassTransit transport header and the outbox
/// row's own <c>CorrelationId</c> column.</para>
/// </remarks>
public sealed record DiscountHistoryAppendedIntegrationEvent(
    string EntityType,
    int EntityId,
    Guid RestaurantId,
    string ChangeType,
    string? OldValues,
    string NewValues) : IntegrationEvent;
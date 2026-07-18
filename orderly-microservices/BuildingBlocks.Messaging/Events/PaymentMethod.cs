namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Closed-discriminator enum for the payment method carried on the
/// wire by <see cref="BasketCheckoutEvent"/> v2 (and any future event
/// that surfaces a payment). Numeric values are the wire-format
/// integers — additions require a coordinated schema-version bump per
/// <see cref="IntegrationEvent.MessageVersion"/>.
/// </summary>
/// <remarks>
/// Lock: <c>Unspecified = 0</c> (sentinel for legacy
/// rows; a fresh event never sets this), <c>Card = 1</c>,
/// <c>Cash = 2</c>, <c>Wallet = 3</c>. New values are additive —
/// MassTransit's <c>System.Text.Json</c> serializer tolerates unknown
/// enum values on the read side, so an old consumer reading a new
/// event still sees a defined enum (the read fallback) and a new
/// consumer reading an old event sees the legacy integer.
/// </remarks>
public enum PaymentMethod
{
    /// <summary>Sentinel for legacy rows where the field was unset.
    /// A fresh event MUST NOT carry this value — the Basket
    /// validator rejects <c>PaymentMethod.Unspecified</c> via
    /// <c>IsInEnum()</c>.</summary>
    Unspecified = 0,

    /// <summary>Card payment — discriminator + brand + last-four
    /// digits travel on the wire as <see cref="PaymentMethodSummary"/>.
    /// Full PAN and CVV stay inside Basket.</summary>
    Card = 1,

    /// <summary>Cash on delivery — only the discriminator + brand +
    /// last-four travel; for cash the brand + last-four are
    /// informational ("brand" = "Cash", "last-four" = "0000").</summary>
    Cash = 2,

    /// <summary>Wallet (Apple Pay, Google Pay, etc.) — the underlying
    /// processor's tokenized reference is captured in the brand field
    /// ("ApplePay", "GooglePay", ...) and last-four is the wallet's
    /// device-account-number suffix.</summary>
    Wallet = 3,
}

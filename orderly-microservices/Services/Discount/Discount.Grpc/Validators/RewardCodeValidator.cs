using Discount.Grpc.Models;
using NodaTime;
using NodaTime.Text;

namespace Discount.Grpc.Validators;

/// <summary>
/// Inline validators for <see cref="RewardCode"/> CRUD + redeem commands.
/// Mirrors the locked §0.3.3 rules. Throws
/// <see cref="BusinessRuleException"/> on violation so the gRPC
/// <c>ExceptionInterceptor</c> maps to <c>StatusCode.InvalidArgument</c>
/// (FluentValidation-style) or <c>StatusCode.FailedPrecondition</c> for
/// business-rule violations (per §0.4.2).
/// </summary>
/// <remarks>
/// <para>The project's gRPC services are direct service classes (no
/// MediatR <c>ICommand</c> pipeline), so FluentValidation's
/// <c>AbstractValidator&lt;T&gt;</c> infrastructure doesn't auto-wire.
/// Phase 5's <c>FeedbackSubmittedConsumer</c> introduces a MediatR
/// <c>ISender</c> dispatch (per §7 Phase 5); at that point a sibling
/// <c>CreateRewardCodeCommand</c> FluentValidation validator ships in
/// lockstep. Until then, the static helpers below centralize the same
/// rule set for the direct gRPC service path.</para>
/// </remarks>
public static class RewardCodeValidator
{
    /// <summary>Maximum <see cref="RewardCode.Code"/> length, per §0.3.3.</summary>
    public const int MaxCodeLength = 120;

    /// <summary>Validate the create / update request shape. Throws on
    /// violation; returns the parsed <see cref="RewardCode"/> on success.
    /// <paramref name="existing"/> is non-null for update paths so the
    /// uniqueness check can ignore the current row.</summary>
    public static RewardCode ValidateAndBuild(
        Guid restaurantId,
        string code,
        RewardKind kind,
        decimal value,
        string? description,
        string? expirationDateIso,
        int? maxRedeemAmount,
        TimeProvider clock,
        RewardCode? existing = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new BusinessRuleException("RewardCode.Code is required.");
        }

        if (code.Length > MaxCodeLength)
        {
            throw new BusinessRuleException(
                $"RewardCode.Code must be ≤ {MaxCodeLength} characters.");
        }

        if (restaurantId == Guid.Empty)
        {
            throw new BusinessRuleException("RewardCode.RestaurantId is required.");
        }

        // Kind-specific value semantics per §0.3.3.
        switch (kind)
        {
            case RewardKind.Percentage:
                if (value <= 0m || value > 100m)
                {
                    throw new BusinessRuleException(
                        "Percentage reward Value must be in (0, 100].");
                }
                break;

            case RewardKind.FixedAmount:
                if (value <= 0m)
                {
                    throw new BusinessRuleException(
                        "FixedAmount reward Value must be > 0.");
                }
                break;

            case RewardKind.Points:
                if (value <= 0m)
                {
                    throw new BusinessRuleException(
                        "Points reward Value must be > 0.");
                }
                break;

            case RewardKind.FreeItem:
                if (value != 0m)
                {
                    throw new BusinessRuleException(
                        "FreeItem reward Value must be 0; the target menu-item " +
                        "id lives in Description as 'free-item:{menuItemId}'.");
                }
                break;

            default:
                throw new BusinessRuleException(
                    $"RewardKind {kind} is not a defined discriminator value.");
        }

        // ExpirationDate: optional, but when set must be strictly future.
        Instant? expirationDate = null;
        if (!string.IsNullOrWhiteSpace(expirationDateIso))
        {
            var parseResult = InstantPattern.ExtendedIso.Parse(expirationDateIso);
            if (!parseResult.Success)
            {
                throw new BusinessRuleException(
                    $"RewardCode.ExpirationDate '{expirationDateIso}' is not a valid ISO-8601 instant.");
            }
            expirationDate = parseResult.Value;
            if (expirationDate <= Instant.FromDateTimeUtc(clock.GetUtcNow().UtcDateTime))
            {
                throw new BusinessRuleException(
                    "RewardCode.ExpirationDate, when set, must be in the future.");
            }
        }

        // MaxRedeemAmount: optional, but when set must be > 0.
        if (maxRedeemAmount is <= 0)
        {
            throw new BusinessRuleException(
                "RewardCode.MaxRedeemAmount, when set, must be > 0.");
        }

        var row = existing ?? new RewardCode
        {
            // C# 11 required modifier (v1.1 M7) — Code + Kind must be set
            // at construction time. The validator is the single point of
            // mutation, so initializing here (rather than at the call site)
            // keeps the contract in one place.
            Code = code,
            Kind = kind,
        };
        row.RestaurantId = restaurantId;
        row.Code = code;
        row.Kind = kind;
        row.Value = value;
        row.Description = description;
        row.ExpirationDate = expirationDate;
        row.MaxRedeemAmount = maxRedeemAmount;
        return row;
    }

    /// <summary>Validate a <see cref="RedeemRewardCodeCommand"/> shape per
    /// §0.3.3: <c>Code</c> non-empty; <c>RestaurantId</c> non-empty; <c>OrderId</c>
    /// non-empty; <c>Quantity</c> ∈ [1, 100]. FreeItem rewards must have
    /// <c>Quantity == 1</c>.</summary>
    public static void ValidateRedeem(
        string code,
        Guid restaurantId,
        Guid orderId,
        int quantity,
        RewardKind? kind = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new BusinessRuleException("RedeemRewardCode.Code is required.");
        }

        if (restaurantId == Guid.Empty)
        {
            throw new BusinessRuleException("RedeemRewardCode.RestaurantId is required.");
        }

        if (orderId == Guid.Empty)
        {
            throw new BusinessRuleException("RedeemRewardCode.OrderId is required.");
        }

        if (quantity < 1 || quantity > 100)
        {
            throw new BusinessRuleException(
                "RedeemRewardCode.Quantity must be in [1, 100].");
        }

        // Per §0.3.3: Kind = FreeItem is excluded from quantity-multi-redemption.
        if (kind == RewardKind.FreeItem && quantity != 1)
        {
            throw new BusinessRuleException(
                "FreeItem rewards cannot be quantity-multi-redeemed (Quantity must be 1).");
        }
    }
}

/// <summary>
/// Thrown by <see cref="RewardCodeValidator"/> on §0.3.3 rule violations.
/// The gRPC <c>ExceptionInterceptor</c> (per §0.4.2) maps this to
/// <c>StatusCode.InvalidArgument</c> for shape failures and
/// <c>StatusCode.FailedPrecondition</c> for business-rule failures.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
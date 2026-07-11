using System.Security.Cryptography;

namespace Catalog.API.Features.CustomerFeedback.SubmitFeedback;

/// <summary>
/// Accepts a customer's post-visit feedback (four ratings + comments) and
/// persists a <see cref="CustomerFeedback"/> row. On
/// <see cref="SubmitFeedbackCommand.OverallRating"/> ≥ 4 a
/// <see cref="FeedbackSubmittedIntegrationEvent"/> is published so the
/// Notification service can issue a reward. Stays in Catalog per
/// (Notification v1 is an out-of-plan prerequisite).
/// </summary>
public record SubmitFeedbackCommand(
    Guid RestaurantId,
    Guid OrderId,
    int OverallRating,
    int FoodQualityRating,
    int ServiceSpeedRating,
    int WaiterFriendlinessRating,
    string? Comments) : ICommand<SubmitFeedbackResult>;

public record SubmitFeedbackResult(int Id, string RewardCode, string RewardType, string RewardDescription, decimal? RewardValue);

public class SubmitFeedbackCommandValidator : AbstractValidator<SubmitFeedbackCommand>
{
    public SubmitFeedbackCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId is required");
        RuleFor(x => x.OverallRating).InclusiveBetween(1, 5).WithMessage("OverallRating must be between 1 and 5");
        RuleFor(x => x.FoodQualityRating).InclusiveBetween(1, 5).WithMessage("FoodQualityRating must be between 1 and 5");
        RuleFor(x => x.ServiceSpeedRating).InclusiveBetween(1, 5).WithMessage("ServiceSpeedRating must be between 1 and 5");
        RuleFor(x => x.WaiterFriendlinessRating).InclusiveBetween(1, 5).WithMessage("WaiterFriendlinessRating must be between 1 and 5");
        RuleFor(x => x.Comments!).MaximumLength(1000).When(x => x.Comments is not null);
    }
}

internal class SubmitFeedbackCommandHandler(
    CatalogDbContext dbContext,
    IOutboxPublisher outbox,
    IFeatureManager featureManager,
    ILogger<SubmitFeedbackCommandHandler> logger) : ICommandHandler<SubmitFeedbackCommand, SubmitFeedbackResult>
{
    /// <summary>
    /// Reward threshold for emitting the integration event. Matches the
    /// ("On <c>OverallRating ≥ 4</c>").
    /// </summary>
    private const int RewardThreshold = 4;

    public async Task<SubmitFeedbackResult> Handle(SubmitFeedbackCommand command, CancellationToken cancellationToken)
    {
        // Generate the reward envelope up front so the row and the
        // integration event stay in sync (both come from the same code
        // path; no risk of "row says no reward but event was published").
        var (rewardType, rewardDescription, rewardValue, rewardCode) = command.OverallRating >= RewardThreshold
            ? GenerateReward(command.RestaurantId)
            : (string.Empty, string.Empty, (decimal?)null, string.Empty);

        var feedback = new Catalog.API.Models.CustomerFeedback
        {
            RestaurantId = command.RestaurantId,
            OrderId = command.OrderId,
            OverallRating = command.OverallRating,
            FoodQualityRating = command.FoodQualityRating,
            ServiceSpeedRating = command.ServiceSpeedRating,
            WaiterFriendlinessRating = command.WaiterFriendlinessRating,
            Comments = command.Comments ?? string.Empty,
            RewardType = rewardType,
            RewardDescription = rewardDescription,
            RewardValue = rewardValue,
            RewardCode = rewardCode,
            RewardRedeemed = false,
            SubmittedAt = SystemClock.Instance.GetCurrentInstant(),
        };

        dbContext.CustomerFeedbacks.Add(feedback);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (command.OverallRating >= RewardThreshold
            && await featureManager.IsEnabledAsync("CatalogFeedbackEvents", cancellationToken).ConfigureAwait(false))
        {
            await outbox.PublishAsync(new FeedbackSubmittedIntegrationEvent
            {
                FeedbackId = feedback.Id,
                RestaurantId = feedback.RestaurantId,
                OrderId = feedback.OrderId,
                OverallRating = feedback.OverallRating,
                Comments = feedback.Comments,
                RewardType = feedback.RewardType,
                RewardDescription = feedback.RewardDescription,
                RewardValue = feedback.RewardValue,
            }, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "FeedbackSubmitted event queued for feedback {FeedbackId} (rating {Rating})",
                feedback.Id, feedback.OverallRating);
        }

        return new SubmitFeedbackResult(feedback.Id, rewardCode, rewardType, rewardDescription, rewardValue);
    }

    /// <summary>
    /// Generates a 10% discount reward. The reward-code is a URL-safe
    /// 12-char base64 string derived from a 9-byte random source. Catalog
    /// only <em>issues</em> the code; the actual reward redemption lives
    /// in Ordering
    /// </summary>
    private static (string RewardType, string RewardDescription, decimal? RewardValue, string RewardCode) GenerateReward(Guid restaurantId)
    {
        Span<byte> bytes = stackalloc byte[9];
        RandomNumberGenerator.Fill(bytes);
        var rewardCode = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return (
            RewardType: "percentage",
            RewardDescription: "10% off your next visit",
            RewardValue: 10m,
            RewardCode: $"R-{restaurantId.ToString()[..8]}-{rewardCode}");
    }
}
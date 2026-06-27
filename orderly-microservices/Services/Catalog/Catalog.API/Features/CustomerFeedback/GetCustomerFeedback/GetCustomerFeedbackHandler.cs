using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.CustomerFeedback.GetCustomerFeedback;

public record GetCustomerFeedbackQuery(
    Guid RestaurantId,
    Guid? OrderId,
    int? MinRating,
    int? MaxRating,
    LocalDate? From,
    LocalDate? To,
    bool? RewardRedeemed,
    int PageIndex = 0,
    int PageSize = 20) : IQuery<GetCustomerFeedbackResult>;

public record GetCustomerFeedbackResult(
    IEnumerable<CustomerFeedbackDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize);

public class GetCustomerFeedbackQueryValidator : AbstractValidator<GetCustomerFeedbackQuery>
{
    public GetCustomerFeedbackQueryValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required");

        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(0).WithMessage("PageIndex must be >= 0");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100");

        RuleFor(x => x.MinRating)
            .InclusiveBetween(1, 5)
            .When(x => x.MinRating.HasValue)
            .WithMessage("MinRating must be between 1 and 5");

        RuleFor(x => x.MaxRating)
            .InclusiveBetween(1, 5)
            .When(x => x.MaxRating.HasValue)
            .WithMessage("MaxRating must be between 1 and 5");

        RuleFor(x => x.MinRating)
            .LessThanOrEqualTo(x => x.MaxRating)
            .When(x => x.MinRating.HasValue && x.MaxRating.HasValue)
            .WithMessage("MinRating must be <= MaxRating");

        RuleFor(x => x.From)
            .LessThanOrEqualTo(x => x.To)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("From date must be before or equal to To date");
    }
}

internal class GetCustomerFeedbackQueryHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetCustomerFeedbackQuery, GetCustomerFeedbackResult>
{
    public async Task<GetCustomerFeedbackResult> Handle(
        GetCustomerFeedbackQuery query,
        CancellationToken cancellationToken)
    {
        var dbQuery = dbContext.CustomerFeedbacks
            .AsNoTracking()
            .Where(x => x.RestaurantId == query.RestaurantId);

        if (query.OrderId.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.OrderId == query.OrderId.Value);
        }

        if (query.MinRating.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.OverallRating >= query.MinRating.Value);
        }

        if (query.MaxRating.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.OverallRating <= query.MaxRating.Value);
        }

        if (query.From.HasValue)
        {
            var fromInstant = query.From.Value.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
            dbQuery = dbQuery.Where(x => x.SubmittedAt >= fromInstant);
        }

        if (query.To.HasValue)
        {
            var toInstant = query.To.Value.PlusDays(1).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
            dbQuery = dbQuery.Where(x => x.SubmittedAt < toInstant);
        }

        if (query.RewardRedeemed.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.RewardRedeemed == query.RewardRedeemed.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var items = await dbQuery
            .OrderByDescending(x => x.SubmittedAt)
            .Skip(query.PageIndex * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Adapt<IEnumerable<CustomerFeedbackDto>>();

        return new GetCustomerFeedbackResult(dtos, totalCount, query.PageIndex, query.PageSize);
    }
}
namespace Ordering.Application.Dashboard.Queries.GetRestaurantDashboardLiveSummary;

public class GetRestaurantDashboardLiveSummaryValidator : AbstractValidator<GetRestaurantDashboardLiveSummaryQuery>
{
    public GetRestaurantDashboardLiveSummaryValidator()
    {
        this.RuleFor(x => x.RestaurantId)
            .NotEmpty()
            .WithMessage("RestaurantId must not be empty.");
    }
}

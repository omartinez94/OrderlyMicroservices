namespace Ordering.Application.Tests.Dashboard;

using FluentValidation.TestHelper;
using Ordering.Application.Dashboard.Queries.GetRestaurantDashboardLiveSummary;

public class GetRestaurantDashboardLiveSummaryValidatorTests
{
    private readonly GetRestaurantDashboardLiveSummaryValidator validator = new();

    [Fact]
    public void Validate_WhenRestaurantIdIsEmpty_ShouldHaveValidationError()
    {
        GetRestaurantDashboardLiveSummaryQuery query = new(Guid.Empty);

        TestValidationResult<GetRestaurantDashboardLiveSummaryQuery> result = this.validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.RestaurantId)
            .WithErrorMessage("RestaurantId must not be empty.");
    }

    [Fact]
    public void Validate_WhenRestaurantIdIsValid_ShouldNotHaveValidationError()
    {
        GetRestaurantDashboardLiveSummaryQuery query = new(Guid.NewGuid());

        TestValidationResult<GetRestaurantDashboardLiveSummaryQuery> result = this.validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }
}

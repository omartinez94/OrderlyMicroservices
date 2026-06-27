using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.CustomerFeedback.GetCustomerFeedbackById;

public record GetCustomerFeedbackByIdQuery(int Id, Guid RestaurantId)
    : IQuery<GetCustomerFeedbackByIdResult>;

public record GetCustomerFeedbackByIdResult(CustomerFeedbackDto Item);

internal class GetCustomerFeedbackByIdQueryHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetCustomerFeedbackByIdQuery, GetCustomerFeedbackByIdResult>
{
    public async Task<GetCustomerFeedbackByIdResult> Handle(
        GetCustomerFeedbackByIdQuery query,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.CustomerFeedbacks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.RestaurantId == query.RestaurantId, cancellationToken)
            ?? throw new CustomerFeedbackNotFoundException(query.Id);

        return new GetCustomerFeedbackByIdResult(item.Adapt<CustomerFeedbackDto>());
    }
}
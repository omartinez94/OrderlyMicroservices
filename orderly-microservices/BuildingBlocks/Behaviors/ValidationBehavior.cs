using BuildingBlocks.CQRS;
using FluentValidation;
using MediatR;

namespace BuildingBlocks.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs every <see cref="IValidator{TRequest}"/>
/// registered for the inbound request before the handler is invoked. Throws
/// <see cref="FluentValidation.ValidationException"/> on the first batch of
/// failures; otherwise the request continues to the handler.
/// </summary>
/// <remarks>
/// The generic constraint was relaxed from <c>ICommand&lt;TResponse&gt;</c> to
/// <c>IRequest&lt;TResponse&gt;</c> as part of the Phase-1 Basket plan: any
/// validator registered against an <see cref="IQuery{TResponse}"/>-shaped
/// request (e.g. <c>Catalog.API.GetMenuItemsQuery</c>, <c>Ordering.API.GetOrdersQuery</c>,
/// <c>Discount.Grpc.EvaluateDiscountRulesQuery</c>) was previously silently
/// skipped by MediatR's open-generic activation. Empty validator lists remain
/// a no-op, so the relaxation is transparent for queries without validators.
/// </remarks>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults.SelectMany(r => r.Errors).Where(f => f != null).ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}

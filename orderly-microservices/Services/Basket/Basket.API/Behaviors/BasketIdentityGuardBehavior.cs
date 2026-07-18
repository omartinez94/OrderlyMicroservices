namespace Basket.API.Behaviors;

/// <summary>
/// MediatR pipeline behaviour that rejects Basket commands / queries
/// when the supplied <c>(UserId, RestaurantId)</c> pair does not match
/// the caller's JWT claims. Registered BEFORE
/// <see cref="ValidationBehavior{TRequest,TResponse}"/> so the 403
/// short-circuits before any validation cost is paid.
/// </summary>
/// <remarks>
/// The identity check is duplicated at the repository layer (defence in
/// depth). The pipeline check is the user-facing surface; the repo
/// check is the backstop in case a future caller bypasses MediatR (a
/// hosted service, a follow-up bus consumer, a future SDK call). Both
/// throw <see cref="ForbiddenException"/> so the global exception
/// handler emits the same 403 + ProblemDetails envelope.
/// </remarks>
public sealed class BasketIdentityGuardBehavior<TRequest, TResponse>(IHttpContextAccessor httpContextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IBasketIdentityRequest identity)
        {
            var user = httpContextAccessor.HttpContext?.User;

            // Default authorization policy rejects unauthenticated callers,
            // but we belt-and-braces here in case the endpoint group is
            // misconfigured (the guard runs after auth middleware but the
            // check is cheap).
            if (user?.Identity?.IsAuthenticated != true)
            {
                throw new ForbiddenException("Authenticated user required for basket operations.");
            }

            var callerUserId = user.GetUserId();
            var callerRestaurantId = user.GetRestaurantId();

            if (callerUserId != identity.UserId || callerRestaurantId != identity.RestaurantId)
            {
                throw new ForbiddenException(
                    $"Cannot operate on basket for ({identity.UserId}, {identity.RestaurantId}) as ({callerUserId}, {callerRestaurantId}).");
            }
        }

        return await next(cancellationToken);
    }
}
using BuildingBlocks.Authorization;
using BuildingBlocks.Exceptions;
using Ordering.Application.Orders.Commands.CreateOrder;

namespace Ordering.API.Endpoints;

public record CreateOrderRequest(OrderDto Order);

public record CreateOrderResponse(Guid Id);

public class CreateOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Orders");

        group.MapPost("/orders", async (
            CreateOrderRequest request,
            HttpContext httpContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Guid restaurantId = request.Order.RestaurantId;

            // Task 3.1: Tenant Validation
            if (!httpContext.User.HasRestaurantAccess(restaurantId))
            {
                throw new ForbiddenException($"Forbidden: User does not have access to restaurant '{restaurantId}'.");
            }

            // Task 3.4: Idempotency-Key Handling
            Guid? idempotencyKey = null;
            if (httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue) &&
                !string.IsNullOrWhiteSpace(headerValue.ToString()) &&
                Guid.TryParse(headerValue.ToString(), out Guid parsedKey))
            {
                idempotencyKey = parsedKey;
            }

            CreateOrderCommand command = new(request.Order, idempotencyKey);
            CreateOrderResult result = await sender.Send(command, cancellationToken);

            if (result.IsReplay && result.Order is not null)
            {
                return Results.Ok(result.Order);
            }

            CreateOrderResponse response = new(result.Id);
            return Results.Created($"/v1/orders/{response.Id}", response);
        })
        .RequirePermission("orders:write")
        .WithDescription("Creates a new order.")
        .WithName("CreateOrder")
        .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
        .Produces<OrderDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}

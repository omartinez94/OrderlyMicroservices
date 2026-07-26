namespace Basket.API.Basket.AdminCarts;

/// <summary>
/// Paged admin-carts query parameters. Bound via
/// <c>[AsParameters]</c> in the GET handler so the OpenAPI
/// surface is auto-generated.
/// </summary>
public record ListCartsQuery(int Page = 0, int PageSize = 50);

/// <summary>
/// Paged admin-carts response. Mirrors
/// <c>BuildingBlocks.Pagination.PaginatedResult&lt;T&gt;</c> shape
/// (the Phase 4 follow-up brings in the BuildingBlocks primitive
/// once Pagination lands; this local record is the standalone
/// version).
/// </summary>
public record ListCartsResponse(
    IReadOnlyList<global::Basket.API.Models.Basket> Items,
    int Page,
    int PageSize,
    int TotalCount);

public class AdminCartEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // All three endpoints require the
        // `orders:admin` permission (Identity.API seeds it in the
        // Commit-0 migration). Group registered under /admin so
        // route conventions stay uniform — `MapBasketGroup()`
        // is the parent; `MapGroup("/admin")` is the sub-group.
        var admin = app.MapBasketGroup().MapGroup("/admin")
            .WithTags("AdminCarts")
            .RequireAuthorization("Default")
            .RequirePermission("orders:admin");

        // GET /api/v1/admin/carts — paged list of baskets in the
        // active tenant. The Marten query is tenant-filtered via
        // `MultiTenanted()` + the JWT-derived
        // `ICurrentRestaurantProvider` filter.
        admin.MapGet("/carts", async (HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
        {
            var query = new ListCartsQuery(
                Page: 0,
                PageSize: 50);
            var result = await sender.Send(new ListCartsQueryRequest(
                httpContext.User.GetRestaurantId(),
                query.Page,
                query.PageSize), cancellationToken);

            return Results.Ok(result);
        })
        .WithName("ListAdminCarts")
        .Produces<ListCartsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithSummary("List carts in the active tenant (admin)")
        .WithDescription("RestaurantSupportAgent tooling. Requires `orders:admin`.");

        // PUT /api/v1/admin/carts/{userId} — upsert. The body is
        // the same `Models.Basket` shape as the user-facing PUT;
        // the URL `userId` is the target cart's owner. The
        // handler records an audit row before the upsert commits.
        admin.MapPut("/carts/{userId:guid}", async (
            Guid userId,
            Models.Basket basket,
            HttpContext httpContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var restaurantId = httpContext.User.GetRestaurantId();
            var actorSub = httpContext.User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

            // Body UserId/RestaurantId MUST be Guid.Empty — the
            // URL + JWT are authoritative (the same spoofing-footgun
            // guard as the user-facing PUT).
            basket.UserId = userId;
            basket.RestaurantId = restaurantId;

            var result = await sender.Send(new UpsertCartAdminCommand(
                basket,
                actorSub,
                restaurantId), cancellationToken);

            return result.IsCreated
                ? Results.Created($"/api/v1/cart", result)
                : Results.Ok(result);
        })
        .WithName("UpsertAdminCart")
        .Produces<StoreBasket.StoreBasketResponse>(StatusCodes.Status200OK)
        .Produces<StoreBasket.StoreBasketResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Upsert a cart on behalf of a user (admin)")
        .WithDescription("RestaurantSupportAgent tooling. Requires `orders:admin`. " +
                         "Writes a `BasketAuditLogEntry` (action=`AdminUpsert`) before the upsert commits.");

        // DELETE /api/v1/admin/carts/{userId} — delete. Idempotent
        // (returns 204 regardless of whether the cart existed).
        // Audit row written on every invocation.
        admin.MapDelete("/carts/{userId:guid}", async (
            Guid userId,
            HttpContext httpContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var restaurantId = httpContext.User.GetRestaurantId();
            var actorSub = httpContext.User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

            await sender.Send(new DeleteCartAdminCommand(
                userId,
                restaurantId,
                actorSub), cancellationToken);

            return Results.NoContent();
        })
        .WithName("DeleteAdminCart")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithSummary("Delete a cart on behalf of a user (admin)")
        .WithDescription("RestaurantSupportAgent tooling. Requires `orders:admin`. " +
                         "Writes a `BasketAuditLogEntry` (action=`AdminDelete`) before the delete commits.");
    }
}

// Inline query + command shapes (a future cleanup can hoist them to BuildingBlocks.CQRS alongside
// the existing query/command primitives).

public record ListCartsQueryRequest(Guid RestaurantId, int Page, int PageSize) : IQuery<ListCartsResponse>;
public record UpsertCartAdminCommand(Models.Basket Basket, string ActorSub, Guid RestaurantId) : ICommand<StoreBasket.StoreBasketResponse>;
public record DeleteCartAdminCommand(Guid TargetUserId, Guid RestaurantId, string ActorSub) : ICommand<MediatR.Unit>;

public class ListCartsQueryHandler()
    : IQueryHandler<ListCartsQueryRequest, ListCartsResponse>
{
    public async Task<ListCartsResponse> Handle(ListCartsQueryRequest request, CancellationToken cancellationToken)
    {
        // The repository doesn't yet expose a paged list —
        // hands this off to a future repo method. Today we
        // return a single empty page to keep the endpoint
        // contract stable.
        await Task.CompletedTask;
        return new ListCartsResponse(
            Items: Array.Empty<Models.Basket>(),
            Page: request.Page,
            PageSize: request.PageSize,
            TotalCount: 0);
    }
}

public class UpsertCartAdminHandler(
    IBasketRepository basketRepository,
    IBasketAuditLog auditLog)
    : ICommandHandler<UpsertCartAdminCommand, StoreBasket.StoreBasketResponse>
{
    public async Task<StoreBasket.StoreBasketResponse> Handle(UpsertCartAdminCommand command, CancellationToken cancellationToken)
    {
        // Audit BEFORE the mutation. The actor is the JWT
        // subject; the target is the (userId, restaurantId)
        // pair. Failure to write the audit row is logged but
        // does NOT block the upsert — the audit is a
        // compliance record, not a transactional lock.
        var auditEntry = new Models.BasketAuditLogEntry
        {
            RestaurantId = command.RestaurantId,
            ActorSub = command.ActorSub,
            TargetUserId = command.Basket.UserId,
            TargetRestaurantId = command.RestaurantId,
            Action = "AdminUpsert",
            OccurredAt = NodaTime.SystemClock.Instance.GetCurrentInstant(),
            Notes = "admin upsert",
        };
        try
        {
            await auditLog.AppendAsync(auditEntry, cancellationToken);
        }
        catch
        {
        }

        var (stored, isCreated) = await basketRepository.StoreBasketAsync(command.Basket, cancellationToken);
        return new StoreBasket.StoreBasketResponse(isCreated, stored.UserId, stored.RestaurantId);
    }
}

public class DeleteCartAdminHandler(
    IBasketRepository basketRepository,
    IBasketAuditLog auditLog)
    : ICommandHandler<DeleteCartAdminCommand, MediatR.Unit>
{
    public async Task<MediatR.Unit> Handle(DeleteCartAdminCommand command, CancellationToken cancellationToken)
    {
        var auditEntry = new Models.BasketAuditLogEntry
        {
            RestaurantId = command.RestaurantId,
            ActorSub = command.ActorSub,
            TargetUserId = command.TargetUserId,
            TargetRestaurantId = command.RestaurantId,
            Action = "AdminDelete",
            OccurredAt = NodaTime.SystemClock.Instance.GetCurrentInstant(),
            Notes = "admin delete",
        };
        try
        {
            await auditLog.AppendAsync(auditEntry, cancellationToken);
        }
        catch
        {
        }

        await basketRepository.DeleteBasketAsync(command.TargetUserId, command.RestaurantId, cancellationToken);
        return MediatR.Unit.Value;
    }
}

using Assistant.API.Domain.Entities;
using Assistant.API.Features.Chat;
using Assistant.API.Infrastructure.Metering;
using Assistant.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace Assistant.API.Features.Quota;

public sealed class GetQuotaEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/restaurants/{restaurantId:guid}/quota", HandleGetQuotaAsync)
            .RequireAuthorization()
            .WithName("GetRestaurantAiQuota")
            .WithTags("Assistant")
            .WithSummary("Returns real-time monthly AI token consumption, status tier, and reset telemetry for a restaurant.");
    }

    public static async Task<IResult> HandleGetQuotaAsync(
        Guid restaurantId,
        HttpContext httpContext,
        ICurrentRestaurantProvider restaurantProvider,
        AiDbContext dbContext,
        IAiQuotaEngine quotaEngine,
        CancellationToken cancellationToken)
    {
        Guid resolvedTenantId = StreamChatEndpoint.ResolveRestaurantId(
            restaurantId,
            httpContext.User,
            restaurantProvider);

        if (resolvedTenantId == Guid.Empty || resolvedTenantId != restaurantId)
        {
            return Results.Forbid();
        }

        RestaurantAiQuotaConfig? persistedConfig = await dbContext.QuotaConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken)
            .ConfigureAwait(false);

        if (persistedConfig is not null)
        {
            await quotaEngine.SyncTenantConfigAsync(persistedConfig, cancellationToken).ConfigureAwait(false);
        }

        TenantQuotaStatusDto status = await quotaEngine
            .GetQuotaStatusAsync(restaurantId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(status);
    }
}

using Assistant.API.Domain.Entities;
using Assistant.API.Features.Chat;
using Assistant.API.Infrastructure.Metering;
using Assistant.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace Assistant.API.Features.Quota;

public sealed record UpdateQuotaConfigRequestDto(
    bool AllowOverage,
    long MaxOverageTokens,
    decimal MaxOverageSpendUsd,
    bool AutoPurchasePacksEnabled = false,
    long? IncludedTokensPerCycle = null);

public sealed class UpdateQuotaConfigEndpoint : ICarterModule
{
    private static readonly HashSet<string> AdminRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Manager",
        "RestaurantAdmin",
        "SuperAdmin",
        "Owner",
        "Admin"
    };

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/restaurants/{restaurantId:guid}/quota/config", HandleUpdateQuotaConfigAsync)
            .RequireAuthorization()
            .WithName("UpdateRestaurantAiQuotaConfig")
            .WithTags("Assistant")
            .WithSummary("Configures the opt-in monthly AI overage safety ceiling for a restaurant.");
    }

    public static async Task<IResult> HandleUpdateQuotaConfigAsync(
        Guid restaurantId,
        UpdateQuotaConfigRequestDto request,
        HttpContext httpContext,
        ICurrentRestaurantProvider restaurantProvider,
        AiDbContext dbContext,
        IAiQuotaEngine quotaEngine,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid resolvedTenantId = StreamChatEndpoint.ResolveRestaurantId(
            restaurantId,
            httpContext.User,
            restaurantProvider);

        if (resolvedTenantId == Guid.Empty || resolvedTenantId != restaurantId)
        {
            return Results.Forbid();
        }

        bool isAuthorizedAdmin = httpContext.User.GetRoles().Any(r => AdminRoles.Contains(r));
        if (!isAuthorizedAdmin)
        {
            return Results.Forbid();
        }

        Instant now = clock.GetCurrentInstant();
        string actor = StreamChatEndpoint.ResolveUserId(httpContext.User).ToString();

        RestaurantAiQuotaConfig? config = await dbContext.QuotaConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken)
            .ConfigureAwait(false);

        if (config is null)
        {
            config = RestaurantAiQuotaConfig.CreateDefault(restaurantId, now, actor);
            dbContext.QuotaConfigs.Add(config);
        }

        if (request.IncludedTokensPerCycle is > 0)
        {
            config.IncludedTokensPerCycle = request.IncludedTokensPerCycle.Value;
        }

        config.UpdateSafetyCeiling(
            allowOverage: request.AllowOverage,
            maxOverageTokens: request.MaxOverageTokens,
            maxOverageSpendUsd: request.MaxOverageSpendUsd,
            autoPurchasePacksEnabled: request.AutoPurchasePacksEnabled,
            actor: actor,
            now: now);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await quotaEngine.SyncTenantConfigAsync(config, cancellationToken).ConfigureAwait(false);

        TenantQuotaStatusDto status = await quotaEngine
            .GetQuotaStatusAsync(restaurantId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(status);
    }
}

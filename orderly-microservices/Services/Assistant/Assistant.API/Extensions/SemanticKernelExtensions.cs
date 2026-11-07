using Assistant.API.Features.Chat;
using Assistant.API.Features.Tools;
using Assistant.API.Features.Tools.ProposalWritePlugins;
using Assistant.API.Features.Tools.ReadPlugins;
using Assistant.API.Infrastructure.Security;
using BuildingBlocks.Http;

namespace Assistant.API.Extensions;

public static class SemanticKernelExtensions
{
    public static IServiceCollection AddOrderlyAiAssistantOrchestration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddTransient<UserJwtPropagationHandler>();

        RegisterInternalHttpClient(services, "Catalog", configuration["DownstreamServices:Catalog"] ?? "http://catalog.api:8080");
        RegisterInternalHttpClient(services, "Ordering", configuration["DownstreamServices:Ordering"] ?? "http://ordering.api:8080");
        RegisterInternalHttpClient(services, "Kitchen", configuration["DownstreamServices:Kitchen"] ?? "http://kitchen.api:8080");
        RegisterInternalHttpClient(services, "Identity", configuration["DownstreamServices:Identity"] ?? "http://identity.api:8080");
        RegisterInternalHttpClient(services, "Assistant", configuration["DownstreamServices:Assistant"] ?? "http://assistant.api:8080");

        services.AddSingleton<RoleToolFilter>();
        services.AddSingleton<IActionProposalSigner, ActionProposalSigner>();

        // Read Tools (Auto-Executed via Direct Internal HTTP with UserJwtPropagationHandler)
        services.AddScoped<IAiToolPlugin, KitchenTicketsReadPlugin>();
        services.AddScoped<IAiToolPlugin, Catalog86ReadPlugin>();
        services.AddScoped<IAiToolPlugin, CatalogMenuReadPlugin>();
        services.AddScoped<IAiToolPlugin, TableStatusReadPlugin>();
        services.AddScoped<IAiToolPlugin, OrderStatusReadPlugin>();
        services.AddScoped<IAiToolPlugin, StaffPermissionsReadPlugin>();
        services.AddScoped<IAiToolPlugin, MarginAnalyticsPlugin>();

        // Proposal-Only Write Tools (Stateless HITL — HMAC-SHA256 signed ActionProposalDto)
        services.AddScoped<IAiToolPlugin, ToggleItem86ProposalPlugin>();
        services.AddScoped<IAiToolPlugin, BumpTicketProposalPlugin>();
        services.AddScoped<IAiToolPlugin, CreateOrderProposalPlugin>();
        services.AddScoped<IAiToolPlugin, ApplyDiscountProposalPlugin>();
        services.AddScoped<IAiToolPlugin, UpdateMenuItemProposalPlugin>();
        services.AddScoped<IAiToolPlugin, StaffRoleProposalPlugin>();
        services.AddScoped<IAiToolPlugin, QuotaAdminProposalPlugin>();

        services.AddScoped<IAiAssistantOrchestrator, TieredAiOrchestrator>();

        return services;
    }

    private static void RegisterInternalHttpClient(
        IServiceCollection services,
        string name,
        string baseAddress)
    {
        services.AddHttpClient(name, client =>
        {
            client.BaseAddress = new Uri(baseAddress);
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<UserJwtPropagationHandler>();
    }
}

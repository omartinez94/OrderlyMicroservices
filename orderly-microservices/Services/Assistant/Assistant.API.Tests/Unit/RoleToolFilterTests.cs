using Assistant.API.Extensions;
using Assistant.API.Features.Tools;
using Assistant.API.Features.Tools.ProposalWritePlugins;
using Assistant.API.Features.Tools.ReadPlugins;
using Assistant.API.Infrastructure.Security;
using FluentAssertions;
using NodaTime;
using NSubstitute;
using Xunit;

namespace Assistant.API.Tests.Unit;

public sealed class RoleToolFilterTests
{
    private readonly IReadOnlyList<IAiToolPlugin> _allTools;
    private readonly RoleToolFilter _filter = new();

    public RoleToolFilterTests()
    {
        IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();
        IActionProposalSigner signer = new ActionProposalSigner("Test-Secret-Key-32Bytes-Minimum!!", SystemClock.Instance);

        _allTools =
        [
            new KitchenTicketsReadPlugin(httpFactory),
            new Catalog86ReadPlugin(httpFactory),
            new CatalogMenuReadPlugin(httpFactory),
            new TableStatusReadPlugin(httpFactory),
            new OrderStatusReadPlugin(httpFactory),
            new StaffPermissionsReadPlugin(httpFactory),
            new MarginAnalyticsPlugin(httpFactory),
            new ToggleItem86ProposalPlugin(signer),
            new BumpTicketProposalPlugin(signer),
            new CreateOrderProposalPlugin(signer),
            new ApplyDiscountProposalPlugin(signer),
            new UpdateMenuItemProposalPlugin(signer),
            new StaffRoleProposalPlugin(signer),
            new QuotaAdminProposalPlugin(signer)
        ];
    }

    [Fact]
    public void FilterToolsForRoles_WhenKitchenStaff_InjectsOnlyKitchenAnd86PluginsAndExcludesAdminAndOrderPlugins()
    {
        IReadOnlyList<IAiToolPlugin> filtered = _filter.FilterToolsForRoles(_allTools, ["KitchenStaff"]);

        IReadOnlyList<string> pluginNames = filtered.Select(t => t.PluginName).ToList();

        pluginNames.Should().BeEquivalentTo(
        [
            "KitchenTicketsReadPlugin",
            "Catalog86ReadPlugin",
            "ToggleItem86ProposalPlugin",
            "BumpTicketProposalPlugin"
        ]);

        pluginNames.Should().NotContain("StaffRoleProposalPlugin");
        pluginNames.Should().NotContain("QuotaAdminProposalPlugin");
        pluginNames.Should().NotContain("CreateOrderProposalPlugin");
        pluginNames.Should().NotContain("MarginAnalyticsPlugin");
    }

    [Fact]
    public void FilterToolsForRoles_WhenWaiter_InjectsFloorAndOrderPluginsAndExcludesAdminAndKitchenWritePlugins()
    {
        IReadOnlyList<IAiToolPlugin> filtered = _filter.FilterToolsForRoles(_allTools, ["Waiter"]);

        IReadOnlyList<string> pluginNames = filtered.Select(t => t.PluginName).ToList();

        pluginNames.Should().BeEquivalentTo(
        [
            "TableStatusReadPlugin",
            "CatalogMenuReadPlugin",
            "Catalog86ReadPlugin",
            "OrderStatusReadPlugin",
            "CreateOrderProposalPlugin",
            "ApplyDiscountProposalPlugin"
        ]);

        pluginNames.Should().NotContain("StaffRoleProposalPlugin");
        pluginNames.Should().NotContain("QuotaAdminProposalPlugin");
        pluginNames.Should().NotContain("MarginAnalyticsPlugin");
        pluginNames.Should().NotContain("ToggleItem86ProposalPlugin");
    }

    [Fact]
    public void FilterToolsForRoles_WhenRestaurantAdminOrManager_InjectsAllPlugins()
    {
        IReadOnlyList<IAiToolPlugin> filtered = _filter.FilterToolsForRoles(_allTools, ["RestaurantAdmin"]);

        filtered.Should().HaveCount(_allTools.Count);
    }
}

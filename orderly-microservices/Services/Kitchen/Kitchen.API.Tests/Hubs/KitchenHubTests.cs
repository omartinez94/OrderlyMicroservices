using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace Kitchen.API.Tests.Hubs;

public sealed class KitchenHubTests
{
    private static readonly Guid DefaultRestaurantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly ILogger<KitchenHub> _logger = NullLogger<KitchenHub>.Instance;
    private readonly IGroupManager _groupManager = Substitute.For<IGroupManager>();
    private readonly HubCallerContext _context = Substitute.For<HubCallerContext>();

    public KitchenHubTests()
    {
        _context.ConnectionId.Returns("conn-test-123");
    }

    [Fact]
    public async Task OnConnectedAsync_WithSingularRestaurantIdClaim_JoinsRestaurantGroup()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantId", restaurantId.ToString())
        ]));
        _context.User.Returns(principal);

        KitchenHub hub = new(_logger)
        {
            Context = _context,
            Groups = _groupManager
        };

        await hub.OnConnectedAsync();

        await _groupManager.Received(1).AddToGroupAsync("conn-test-123", $"restaurant:{restaurantId}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_WithPluralRestaurantIdsClaim_JoinsRestaurantGroup()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantIds", restaurantId.ToString())
        ]));
        _context.User.Returns(principal);

        KitchenHub hub = new(_logger)
        {
            Context = _context,
            Groups = _groupManager
        };

        await hub.OnConnectedAsync();

        await _groupManager.Received(1).AddToGroupAsync("conn-test-123", $"restaurant:{restaurantId}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_WithBothClaimsAndDuplicates_JoinsDistinctRestaurantGroups()
    {
        Guid restaurantId1 = Guid.NewGuid();
        Guid restaurantId2 = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantId", restaurantId1.ToString()),
            new Claim("restaurantIds", restaurantId1.ToString()),
            new Claim("restaurantIds", restaurantId2.ToString())
        ]));
        _context.User.Returns(principal);

        KitchenHub hub = new(_logger)
        {
            Context = _context,
            Groups = _groupManager
        };

        await hub.OnConnectedAsync();

        await _groupManager.Received(1).AddToGroupAsync("conn-test-123", $"restaurant:{restaurantId1}", Arg.Any<CancellationToken>());
        await _groupManager.Received(1).AddToGroupAsync("conn-test-123", $"restaurant:{restaurantId2}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_WithoutClaims_JoinsDefaultRestaurantGroup()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity());
        _context.User.Returns(principal);

        KitchenHub hub = new(_logger)
        {
            Context = _context,
            Groups = _groupManager
        };

        await hub.OnConnectedAsync();

        await _groupManager.Received(1).AddToGroupAsync("conn-test-123", $"restaurant:{DefaultRestaurantId}", Arg.Any<CancellationToken>());
    }
}

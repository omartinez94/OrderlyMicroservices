using BuildingBlocks.Pagination;
using Kitchen.API.Application.Dtos;
using Kitchen.API.Application.KitchenTickets.Queries;

namespace Kitchen.API.Tests.Queries;

/// <summary>
/// Covers the read side of <c>GET /v1/kitchen/queue</c>.
///
/// <para><b>Plan deviation (E2E_TEST_COVERAGE_PLAN §6.4).</b> The plan lists
/// <c>Handle_ReturnsTicketsSortedByPriority()</c>. Ordering is not a handler
/// concern: <c>IKitchenTicketRepository.GetQueueAsync</c> owns the
/// <c>ReceivedAt</c>-ascending sort at the SQL boundary, and the handler
/// projects the rows in the order it receives them. That test is replaced by
/// <see cref="Handle_PreservesRepositoryOrdering"/> (projection is
/// order-preserving) plus <see cref="Handle_ForwardsFiltersAndPagingToRepository"/>,
/// which pins the filter/skip/take contract the repository sort depends on.</para>
/// </summary>
public sealed class GetKitchenQueueQueryHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket TicketReceivedAt(Instant receivedAt, string orderNumber)
    {
        OrderItemSeed seed = new OrderItemSeed(
            OrderItemId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            MenuItemName: "Burger",
            Quantity: 1,
            UnitPrice: 9.99m,
            SelectedVariations: [],
            Customizations: [],
            SpecialInstructions: null,
            SeatNumber: null);

        return KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: orderNumber,
            itemSeeds: [seed],
            notes: string.Empty,
            receivedAt: receivedAt);
    }

    private static (GetKitchenQueueHandler handler, IKitchenTicketRepository repo) BuildHandler(
        IReadOnlyList<KitchenTicket> rows)
    {
        IKitchenTicketRepository repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetQueueAsync(
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(rows);

        return (new GetKitchenQueueHandler(repo), repo);
    }

    [Fact]
    public async Task Handle_PreservesRepositoryOrdering()
    {
        // The repository returns oldest-first; the DTO projection must not
        // reshuffle the sequence.
        List<KitchenTicket> rows =
        [
            TicketReceivedAt(Now, "ORD-2026-0001"),
            TicketReceivedAt(Now.Plus(Duration.FromMinutes(5)), "ORD-2026-0002"),
            TicketReceivedAt(Now.Plus(Duration.FromMinutes(10)), "ORD-2026-0003"),
        ];
        (GetKitchenQueueHandler handler, IKitchenTicketRepository _) = BuildHandler(rows);

        PaginatedResult<KitchenTicketDto> result = await handler.Handle(
            new GetKitchenQueueQuery(RestaurantId: null, StationId: null),
            CancellationToken.None);

        result.Data.Select(d => d.OrderNumber)
            .Should().ContainInOrder("ORD-2026-0001", "ORD-2026-0002", "ORD-2026-0003");
        result.Count.Should().Be(3);
    }

    [Theory]
    // page below 1 clamps to 1; pageSize below 1 falls back to the 50 default.
    [InlineData(0, 0, 1, 50)]
    [InlineData(-5, -1, 1, 50)]
    // pageSize above the 200 ceiling clamps down.
    [InlineData(1, 500, 1, 200)]
    // in-range values pass through untouched.
    [InlineData(3, 25, 3, 25)]
    public async Task Handle_ClampsPageAndPageSize(
        int requestedPage,
        int requestedPageSize,
        int expectedPage,
        int expectedPageSize)
    {
        (GetKitchenQueueHandler handler, IKitchenTicketRepository repo) = BuildHandler([]);

        PaginatedResult<KitchenTicketDto> result = await handler.Handle(
            new GetKitchenQueueQuery(
                RestaurantId: null,
                StationId: null,
                Page: requestedPage,
                PageSize: requestedPageSize),
            CancellationToken.None);

        result.PageIndex.Should().Be(expectedPage);
        result.PageSize.Should().Be(expectedPageSize);

        int expectedSkip = (expectedPage - 1) * expectedPageSize;
        await repo.Received(1).GetQueueAsync(
            null,
            null,
            expectedSkip,
            expectedPageSize,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForwardsFiltersAndPagingToRepository()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid stationId = Guid.NewGuid();
        (GetKitchenQueueHandler handler, IKitchenTicketRepository repo) = BuildHandler([]);

        await handler.Handle(
            new GetKitchenQueueQuery(restaurantId, stationId, Page: 2, PageSize: 10),
            CancellationToken.None);

        await repo.Received(1).GetQueueAsync(
            restaurantId,
            stationId,
            10,
            10,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyQueue_ReturnsEmptyResult()
    {
        (GetKitchenQueueHandler handler, IKitchenTicketRepository _) = BuildHandler([]);

        PaginatedResult<KitchenTicketDto> result = await handler.Handle(
            new GetKitchenQueueQuery(RestaurantId: null, StationId: null),
            CancellationToken.None);

        result.Data.Should().BeEmpty();
        result.Count.Should().Be(0);
    }
}

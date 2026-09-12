using System.Net;
using System.Net.Http.Json;
using Kitchen.API.Application.EventHandlers.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Kitchen.API.Tests.Integration;

/// <summary>
/// Phase 4 (E2E_TEST_COVERAGE_PLAN §6.4) — drives a kitchen ticket through
/// its full state machine over the real HTTP pipeline against the
/// Testcontainers Postgres, then verifies the persisted aggregate.
///
/// <para>Transitions are asserted by HTTP status code; final state is read
/// back through <see cref="IKitchenTicketRepository"/> rather than by
/// deserializing the response body, because <c>KitchenTicketDto</c> carries
/// NodaTime <c>Instant</c> members that the bare test <c>HttpClient</c> has
/// no converters for. Queue membership is asserted against the raw JSON
/// string for the same reason.</para>
///
/// <para><b>Plan deviation (§6.4).</b> The plan lists
/// <c>SignalR_TicketAccepted_BroadcastReceived()</c> "if WAF supports SignalR
/// test client". It does not: the hub is behind the JWT bearer scheme that
/// <c>TestAuthHandler</c> replaces via request headers, which a
/// <c>HubConnection</c> negotiate handshake does not carry, so the test would
/// assert nothing useful. Broadcast mapping for every domain event —
/// <c>TicketAccepted</c> included — is already covered exhaustively by
/// <c>Hubs/KitchenTicketBroadcasterTests</c>. It is replaced here by
/// <see cref="Queue_ExcludesBumpedTicket"/>, which pins the queue
/// projection's status filter (New/InProgress only) end to end.</para>
/// </summary>
[Collection(nameof(KitchenWebApplicationFactoryCollection))]
public sealed class KitchenLifecycleIntegrationTests(KitchenWebApplicationFactory factory)
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private HttpClient NewClient()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions",
            "kitchen:view_orders,kitchen:update_prep_status");
        return client;
    }

    /// <summary>
    /// Unique per call so tests sharing the collection fixture (and therefore
    /// the database) never collide on the order-number assertions.
    /// </summary>
    private static string NewOrderNumber() => $"ORD-LC-{Guid.NewGuid():N}"[..18];

    private static KitchenTicket NewTicket(string orderNumber, int itemCount = 1)
    {
        List<OrderItemSeed> seeds = [];
        for (int i = 0; i < itemCount; i++)
        {
            seeds.Add(new OrderItemSeed(
                OrderItemId: Guid.NewGuid(),
                MenuItemId: Guid.NewGuid(),
                MenuItemName: $"Burger {i}",
                Quantity: 1,
                UnitPrice: 9.99m,
                SelectedVariations: [],
                Customizations: [],
                SpecialInstructions: null,
                SeatNumber: null));
        }

        return KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: orderNumber,
            itemSeeds: seeds,
            notes: string.Empty,
            receivedAt: Now);
    }

    /// <summary>
    /// Commits a ticket on its own scope so the subsequent HTTP requests
    /// (each on a fresh scope) observe it. Same shape as
    /// <c>OutboxPublishTests.SeedTicketAsync</c>.
    /// </summary>
    private async Task<KitchenTicket> SeedTicketAsync(KitchenTicket ticket)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IKitchenTicketRepository repo = scope.ServiceProvider.GetRequiredService<IKitchenTicketRepository>();
        IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await repo.AddAsync(ticket);
        await uow.SaveChangesAsync();
        return ticket;
    }

    private async Task<KitchenTicket> ReloadAsync(Guid ticketId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IKitchenTicketRepository repo = scope.ServiceProvider.GetRequiredService<IKitchenTicketRepository>();
        KitchenTicket? reloaded = await repo.GetByIdAsync(ticketId, CancellationToken.None);
        reloaded.Should().NotBeNull("the ticket must still exist after the transition");
        return reloaded!;
    }

    private static async Task PostAndExpectNoContentAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.PostAsync(url, content: null);
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, $"POST {url} — body: {body}");
    }

    [Fact]
    public async Task FullLifecycle_NewToBumped()
    {
        KitchenTicket ticket = await SeedTicketAsync(NewTicket(NewOrderNumber(), itemCount: 2));
        Guid id = ticket.Id.Value;
        List<Guid> itemIds = ticket.Items.Select(i => i.Id.Value).ToList();
        HttpClient client = NewClient();

        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/accept");
        (await ReloadAsync(id)).Status.Should().Be(KitchenTicketStatus.InProgress);

        foreach (Guid itemId in itemIds)
        {
            await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/items/{itemId}/start");
            await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/items/{itemId}/ready");
        }

        // Every item ready, but the aggregate stays InProgress until the
        // explicit mark-ready command.
        KitchenTicket afterItems = await ReloadAsync(id);
        afterItems.Status.Should().Be(KitchenTicketStatus.InProgress);
        afterItems.Items.Should().OnlyContain(i => i.Status == KitchenItemStatus.Ready);

        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/mark-ready");
        (await ReloadAsync(id)).Status.Should().Be(KitchenTicketStatus.Ready);

        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/bump");
        KitchenTicket bumped = await ReloadAsync(id);
        bumped.Status.Should().Be(KitchenTicketStatus.Bumped);
        bumped.BumpedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Recall_FromBumped_BackToReady()
    {
        KitchenTicket ticket = await SeedTicketAsync(NewTicket(NewOrderNumber()));
        Guid id = ticket.Id.Value;
        Guid itemId = ticket.Items.First().Id.Value;
        HttpClient client = NewClient();

        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/accept");
        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/items/{itemId}/start");
        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/items/{itemId}/ready");
        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/mark-ready");
        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/bump");

        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/recall");

        KitchenTicket recalled = await ReloadAsync(id);
        recalled.Status.Should().Be(KitchenTicketStatus.Ready);
        recalled.BumpedAt.Should().BeNull("recall clears the bump timestamp");

        // Recall is only legal from Bumped — a second recall must 409.
        HttpResponseMessage second = await client.PostAsync(
            $"/v1/kitchen/tickets/{id}/recall", content: null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_FromActiveState_Returns204_AndRecordsReason()
    {
        KitchenTicket ticket = await SeedTicketAsync(NewTicket(NewOrderNumber()));
        Guid id = ticket.Id.Value;
        HttpClient client = NewClient();

        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/accept");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/v1/kitchen/tickets/{id}/cancel",
            new { reason = "86'd the special" });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        KitchenTicket cancelled = await ReloadAsync(id);
        cancelled.Status.Should().Be(KitchenTicketStatus.Cancelled);
        cancelled.CancellationReason.Should().Be("86'd the special");
        cancelled.CancelledByUserId.Should().NotBeNull();
        cancelled.CancelledAt.Should().NotBeNull();

        // Cancelled is terminal — a second cancel must 409.
        HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/v1/kitchen/tickets/{id}/cancel",
            new { reason = "again" });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateTicketFromOrderCreated_IntegrationEvent()
    {
        string orderNumber = NewOrderNumber();
        Guid orderId = Guid.NewGuid();

        OrderCreatedIntegrationEvent evt = new OrderCreatedIntegrationEvent
        {
            OrderId = orderId,
            OrderNumber = orderNumber,
            RestaurantId = Guid.NewGuid(),
            TableId = null,
            OrderType = 1,
            CustomerId = Guid.NewGuid(),
            Subtotal = 9.99m,
            TotalAmount = 9.99m,
            TaxAmount = 0m,
            DiscountAmount = 0m,
            Currency = "USD",
            DiscountCode = null,
            BillingAddress = new OrderAddress("1 Test St", "Testville", "TS", "00000", "US"),
            DeliveryAddress = null,
            Items = [new KitchenOrderItemPreview(
                OrderItemId: Guid.NewGuid(),
                MenuItemId: Guid.NewGuid(),
                MenuItemName: "Burger",
                Quantity: 1,
                UnitPrice: 9.99m,
                SelectedVariations: [],
                Customizations: [],
                SpecialInstructions: null,
                SeatNumber: null)],
            EstimatedPrepTimeMinutes = 15,
            Notes = string.Empty,
            OccurredOn = Now,
        };

        await ConsumeAsync(evt);

        // The consumer creates the ticket in New status, so it must surface
        // on the queue endpoint.
        KitchenTicket created = await ReloadAsync(orderId);
        created.Status.Should().Be(KitchenTicketStatus.New);
        created.OrderNumber.Should().Be(orderNumber);

        HttpResponseMessage response = await NewClient().GetAsync("/v1/kitchen/queue?pageSize=200");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(orderNumber,
            "a ticket created from OrderCreatedIntegrationEvent must appear in the kitchen queue");
    }

    [Fact]
    public async Task Queue_ExcludesBumpedTicket()
    {
        string orderNumber = NewOrderNumber();
        KitchenTicket ticket = await SeedTicketAsync(NewTicket(orderNumber));
        Guid id = ticket.Id.Value;
        Guid itemId = ticket.Items.First().Id.Value;
        HttpClient client = NewClient();

        // New tickets are on the queue.
        string beforeBody = await (await client.GetAsync("/v1/kitchen/queue?pageSize=200"))
            .Content.ReadAsStringAsync();
        beforeBody.Should().Contain(orderNumber);

        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/accept");
        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/items/{itemId}/start");
        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/items/{itemId}/ready");
        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/mark-ready");
        await PostAndExpectNoContentAsync(client, $"/v1/kitchen/tickets/{id}/bump");

        // The queue projection filters to New/InProgress, so a Bumped ticket
        // must have dropped off.
        HttpResponseMessage after = await client.GetAsync("/v1/kitchen/queue?pageSize=200");
        after.StatusCode.Should().Be(HttpStatusCode.OK);
        string afterBody = await after.Content.ReadAsStringAsync();
        afterBody.Should().NotContain(orderNumber,
            "a Bumped ticket is no longer part of the active kitchen queue");
    }

    /// <summary>
    /// Invokes the inbound consumer in-process on a fresh scope — the repo
    /// convention set by <see cref="DuplicateOrderCreatedTests"/>.
    /// </summary>
    private async Task ConsumeAsync(OrderCreatedIntegrationEvent message)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        OrderCreatedIntegrationEventHandler handler = new OrderCreatedIntegrationEventHandler(
            sp.GetRequiredService<IKitchenTicketRepository>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<IHubContext<KitchenHub, IKitchenHubClient>>(),
            NullLogger<OrderCreatedIntegrationEventHandler>.Instance);

        ConsumeContext<OrderCreatedIntegrationEvent> context =
            Substitute.For<ConsumeContext<OrderCreatedIntegrationEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);

        await handler.Consume(context);
    }
}

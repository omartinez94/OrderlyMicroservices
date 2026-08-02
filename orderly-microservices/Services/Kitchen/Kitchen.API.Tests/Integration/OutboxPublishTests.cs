using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Messaging.Events;
using Kitchen.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kitchen.API.Tests.Integration;

/// <summary>
/// Phase 3 (persistence/reliability plan §6.3) verification: every
/// Kitchen command that drives an aggregate-level transition stages the
/// matching outbound integration event in the <c>outbox_messages</c> table
/// inside the same transaction as the ticket mutation. The
/// <c>KitchenOutboxDispatcher</c> hosted service (active in the test
/// host because <c>Outbox:Enabled</c> defaults to <c>true</c>) then
/// relays the row onto the broker, stamping <c>DispatchedAt</c>.
///
/// This file locks the publisher half of the contract for all 5
/// command handlers: AcceptOrder / BumpOrder / CancelOrder /
/// MarkOrderReady / StartItemPrep. The duplicate-event guard for the
/// inbound <c>OrderCreatedIntegrationEvent</c> consumer lives in
/// <see cref="DuplicateOrderCreatedTests"/>.
/// </summary>
[Collection(nameof(KitchenWebApplicationFactoryCollection))]
public sealed class OutboxPublishTests(KitchenWebApplicationFactory factory)
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private HttpClient NewClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions",
            "kitchen:view_orders,kitchen:update_prep_status");
        return client;
    }

    private static KitchenTicket NewTicket()
    {
        var seed = new OrderItemSeed(
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
            orderNumber: "ORD-2026-0001",
            itemSeeds: [seed],
            notes: string.Empty,
            receivedAt: Now);
    }

    private static KitchenTicket ReadyTicket()
    {
        var ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        foreach (var item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Now);
            ticket.MarkItemReady(item.Id, Now);
        }
        ticket.MarkReady(Now);
        return ticket;
    }

    private static KitchenTicket InProgressTicketWithAllItemsReady()
    {
        var ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        foreach (var item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Now);
            ticket.MarkItemReady(item.Id, Now);
        }
        return ticket;
    }

    /// <summary>
    /// Seeds a ticket via the ambient repository and commits so the HTTP
    /// command request sees it on its own scope. Mirrors what the
    /// <c>OrderCreatedIntegrationEventHandler</c> consumer would do, but
    /// bypasses the bus surface so each test owns its ticket lifecycle.
    /// </summary>
    private async Task<KitchenTicket> SeedTicketAsync(KitchenTicket ticket)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKitchenTicketRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await repo.AddAsync(ticket);
        await uow.SaveChangesAsync();
        return ticket;
    }

    [Fact]
    public async Task AcceptOrder_StagesAcceptedEvent_AndDispatcherRelaysIt()
    {
        var ticket = await SeedTicketAsync(NewTicket());

        var response = await NewClient().PostAsync(
            $"/api/v1/kitchen/tickets/{ticket.Id.Value}/accept",
            content: null);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, $"body: {body}");

        var row = await WaitForDispatchedOutboxRowAsync(
            typeof(KitchenOrderAcceptedIntegrationEvent).AssemblyQualifiedName!,
            ticket.Id.Value);
        row.Should().NotBeNull("the publisher must stage the Accepted event");
    }

    [Fact]
    public async Task BumpOrder_StagesBumpedEvent_AndDispatcherRelaysIt()
    {
        var ticket = await SeedTicketAsync(ReadyTicket());

        var response = await NewClient().PostAsync(
            $"/api/v1/kitchen/tickets/{ticket.Id.Value}/bump",
            content: null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = await WaitForDispatchedOutboxRowAsync(
            typeof(KitchenOrderBumpedIntegrationEvent).AssemblyQualifiedName!,
            ticket.Id.Value);
        row.Should().NotBeNull("the publisher must stage the Bumped event");
    }

    [Fact]
    public async Task CancelOrder_StagesCancelledEvent_AndDispatcherRelaysIt()
    {
        var ticket = await SeedTicketAsync(NewTicket());

        var response = await NewClient().PostAsJsonAsync(
            $"/api/v1/kitchen/tickets/{ticket.Id.Value}/cancel",
            new { reason = "out of stock" });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = await WaitForDispatchedOutboxRowAsync(
            typeof(KitchenOrderCancelledIntegrationEvent).AssemblyQualifiedName!,
            ticket.Id.Value);
        row.Should().NotBeNull("the publisher must stage the Cancelled event");
    }

    [Fact]
    public async Task MarkOrderReady_StagesReadyEvent_AndDispatcherRelaysIt()
    {
        var ticket = await SeedTicketAsync(InProgressTicketWithAllItemsReady());

        var response = await NewClient().PostAsync(
            $"/api/v1/kitchen/tickets/{ticket.Id.Value}/mark-ready",
            content: null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = await WaitForDispatchedOutboxRowAsync(
            typeof(KitchenOrderReadyIntegrationEvent).AssemblyQualifiedName!,
            ticket.Id.Value);
        row.Should().NotBeNull("the publisher must stage the Ready event");
    }

    [Fact]
    public async Task StartItemPrep_FirstStart_StagesPrepStartedEvent_AndDispatcherRelaysIt()
    {
        var ticket = await SeedTicketAsync(NewTicket());
        var firstItemId = ticket.Items.First().Id.Value;

        var response = await NewClient().PostAsync(
            $"/api/v1/kitchen/tickets/{ticket.Id.Value}/items/{firstItemId}/start",
            content: null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = await WaitForDispatchedOutboxRowAsync(
            typeof(KitchenOrderPrepStartedIntegrationEvent).AssemblyQualifiedName!,
            ticket.Id.Value);
        row.Should().NotBeNull(
            "the publisher must stage the PrepStarted event on the first item start");
    }

    /// <summary>
    /// Polls the <c>outbox_messages</c> table for a row whose payload
    /// references the supplied <paramref name="ticketId"/> and whose
    /// <c>Type</c> matches <paramref name="eventType"/>. Returns the row
    /// once <c>DispatchedAt</c> is non-null (the
    /// <c>OutboxOptions.ActivePollInterval</c> default of 1s bounds the
    /// wall-clock to a few seconds in the happy path).
    /// </summary>
    private async Task<OutboxMessage?> WaitForDispatchedOutboxRowAsync(
        string eventType,
        Guid ticketId)
    {
        // Bound the wait to ~10s. The dispatcher polls every 1s when
        // there's work; on a slow CI runner we still want a deterministic
        // pass/fail rather than a hung test.
        for (int i = 0; i < 20; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KitchenDbContext>();
            var row = await db.OutboxMessages
                .Where(m => m.Type == eventType)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Payload.Contains(ticketId.ToString()));

            if (row is not null && row.DispatchedAt is not null)
            {
                return row;
            }

            await Task.Delay(500);
        }

        return null;
    }
}

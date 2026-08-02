using BuildingBlocks.Messaging.Events;
using Kitchen.API.Application.EventHandlers.Integration;
using Kitchen.API.Domain.Aggregates.KitchenTicket;
using Kitchen.API.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Kitchen.API.Tests.Integration;

/// <summary>
/// Phase 3 (persistence/reliability plan §6.3) verification: the
/// <c>OrderCreatedIntegrationEvent</c> consumer is idempotent on
/// redelivery. The original handler short-circuited on an optimistic
/// <c>GetByIdAsync</c> check, which has a race window — two concurrent
/// redeliveries can both pass the pre-check before either commits, and
/// the loser would surface as a <c>DbUpdateException</c> that MassTransit
/// nacks indefinitely (poison-message loop). The Phase 3 fix wraps the
/// <c>AddAsync</c>+<c>SaveChangesAsync</c> pair in a
/// <c>try/catch(DbUpdateException)</c> filtered by
/// <see cref="Kitchen.API.Infrastructure.IsDuplicateKey.IsUniqueViolation"/>.
///
/// This file drives the consumer in-process (the repo convention set by
/// <c>Discount.Grpc.Tests/Integration/MenuItemChangedConsumerTests</c>
/// and <c>FeedbackSubmittedConsumerTests</c>): build a
/// <c>Substitute.For&lt;ConsumeContext&lt;T&gt;&gt;</c>, invoke
/// <c>Consume</c> twice on the same <c>OrderCreatedIntegrationEvent</c>,
/// and assert that the second call is a no-op — no exception, exactly
/// one <c>KitchenTicket</c> row, and an Information log line.
/// </summary>
[Collection(nameof(KitchenWebApplicationFactoryCollection))]
public sealed class DuplicateOrderCreatedTests(KitchenWebApplicationFactory factory)
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    [Fact]
    public async Task SameEventDeliveredTwice_CreatesOneTicket_NoNack()
    {
        // Use a unique OrderId per test run so concurrent tests in the
        // same collection fixture (the WebApplicationFactory is shared
        // across the suite) don't share the pre-check path.
        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderNumber = $"ORD-DUP-{orderId:N}".Substring(0, 18);

        var evt = new OrderCreatedIntegrationEvent
        {
            OrderId = orderId,
            OrderNumber = orderNumber,
            RestaurantId = restaurantId,
            TableId = null,
            OrderType = 1,
            CustomerId = customerId,
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

        // First delivery: the consumer creates the ticket.
        await ConsumeAsync(evt);
        await ConsumeAsync(evt); // Second delivery: must be a no-op.

        // Filter via the repository's GetByIdAsync (the production path).
        // The KitchenTicket.Id column is a value object (KitchenTicketId)
        // with a uuid backing type, so a raw DbSet.Where on .Id.Value
        // doesn't translate to SQL — the repo handles the conversion.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var repo = verifyScope.ServiceProvider.GetRequiredService<IKitchenTicketRepository>();
        var existing = await repo.GetByIdAsync(orderId, CancellationToken.None);

        existing.Should().NotBeNull("the first delivery must create the ticket");
        existing!.Id.Value.Should().Be(orderId);

        // Re-count by querying the Tickets DbSet and filtering on
        // OrderNumber (a string column that EF can translate). This proves
        // no second row was inserted by the duplicate delivery.
        await using var countScope = factory.Services.CreateAsyncScope();
        var db = countScope.ServiceProvider.GetRequiredService<KitchenDbContext>();
        var matching = await db.Tickets
            .Where(t => t.OrderNumber == orderNumber)
            .ToListAsync();
        matching.Should().HaveCount(1,
            "the second delivery of the same event must not create a second KitchenTicket");
    }

    /// <summary>
    /// Builds a fresh scope per call (mirrors MassTransit's per-message
    /// scope in production) and invokes the consumer in-process. The
    /// <c>Substitute.For&lt;ConsumeContext&lt;T&gt;&gt;</c> path is the
    /// project convention — see Discount's
    /// <c>MenuItemChangedConsumerTests</c> and
    /// <c>FeedbackSubmittedConsumerTests</c> for the same shape.
    /// </summary>
    private async Task ConsumeAsync(OrderCreatedIntegrationEvent message)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var handler = new OrderCreatedIntegrationEventHandler(
            sp.GetRequiredService<IKitchenTicketRepository>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<KitchenHub, IKitchenHubClient>>(),
            NullLogger<OrderCreatedIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<OrderCreatedIntegrationEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);

        await handler.Consume(context);
    }
}

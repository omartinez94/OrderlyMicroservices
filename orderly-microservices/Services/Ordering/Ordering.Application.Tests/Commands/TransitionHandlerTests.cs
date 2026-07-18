using Ordering.Application.Orders.Commands.CancelOrder;
using Ordering.Application.Orders.Commands.MarkItemReady;
using Ordering.Application.Orders.Commands.MarkOrderDelivered;
using Ordering.Application.Orders.Commands.MarkOrderReady;
using Ordering.Application.Orders.Commands.StartItemPrep;
using Ordering.Application.Orders.Commands.StartOrderPrep;

namespace Ordering.Application.Tests.Commands;

/// <summary>
/// Locks in the contract for the four state-transition handlers that
/// don't need the auth context (StartOrderPrep, MarkOrderReady,
/// MarkOrderDelivered, StartItemPrep, MarkItemReady) and the
/// reason-taking CancelOrder handler. Each test verifies the aggregate
/// mutates correctly and the DbContext persists.
/// </summary>
public sealed class TransitionHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order SeedOrder(OrderStatus status, PrepStatus itemStatus = PrepStatus.Pending)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, status);
        order.Add(NewMenuItemId(), quantity: 1, price: 5m);
        order.OrderItems.Single().PrepStatus = itemStatus;
        order.ClearDomainEvents();
        return order;
    }

    private static IApplicationDbContext NewDbContextWith(Order order)
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(order));
        return dbContext;
    }

    [Fact]
    public async Task StartOrderPrepHandler_FromConfirmed_TransitionsToPreparing()
    {
        var order = SeedOrder(OrderStatus.Confirmed);
        var dbContext = NewDbContextWith(order);
        var handler = new StartOrderPrepHandler(dbContext);

        await handler.Handle(new StartOrderPrepCommand(order.Id.Value), CancellationToken.None);

        order.Status.Should().Be(OrderStatus.Preparing);
        order.PreparingStartedAt.Should().NotBeNull();
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartOrderPrepHandler_UnknownOrder_ThrowsNotFound()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));
        var handler = new StartOrderPrepHandler(dbContext);

        Func<Task> act = () => handler.Handle(
            new StartOrderPrepCommand(Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }

    [Fact]
    public async Task MarkOrderReadyHandler_FromPreparing_TransitionsToReady()
    {
        var order = SeedOrder(OrderStatus.Preparing);
        var dbContext = NewDbContextWith(order);
        var handler = new MarkOrderReadyHandler(dbContext);

        await handler.Handle(new MarkOrderReadyCommand(order.Id.Value), CancellationToken.None);

        order.Status.Should().Be(OrderStatus.Ready);
        order.ReadyAt.Should().NotBeNull();
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkOrderDeliveredHandler_FromReady_TransitionsToDelivered()
    {
        var order = SeedOrder(OrderStatus.Ready);
        var dbContext = NewDbContextWith(order);
        var handler = new MarkOrderDeliveredHandler(dbContext);

        await handler.Handle(
            new MarkOrderDeliveredCommand(order.Id.Value),
            CancellationToken.None);

        order.Status.Should().Be(OrderStatus.Delivered);
        order.DeliveredAt.Should().NotBeNull();
        order.DeliveryStatus.Should().Be(DeliveryStatus.Delivered);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartItemPrepHandler_FromPending_TransitionsItemToPreparing()
    {
        var order = SeedOrder(OrderStatus.Confirmed, PrepStatus.Pending);
        var dbContext = NewDbContextWith(order);
        var handler = new StartItemPrepHandler(dbContext);
        var itemId = order.OrderItems.Single().Id.Value;

        await handler.Handle(
            new StartItemPrepCommand(order.Id.Value, itemId),
            CancellationToken.None);

        var item = order.OrderItems.Single();
        item.PrepStatus.Should().Be(PrepStatus.Preparing);
        item.PrepStartedAt.Should().NotBeNull();
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartItemPrepHandler_UnknownItem_ThrowsOrderItemNotFound()
    {
        var order = SeedOrder(OrderStatus.Confirmed, PrepStatus.Pending);
        var dbContext = NewDbContextWith(order);
        var handler = new StartItemPrepHandler(dbContext);

        Func<Task> act = () => handler.Handle(
            new StartItemPrepCommand(order.Id.Value, Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderItemNotFoundException>();
    }

    [Fact]
    public async Task MarkItemReadyHandler_FromPreparing_TransitionsItemToReady()
    {
        var order = SeedOrder(OrderStatus.Preparing, PrepStatus.Preparing);
        var dbContext = NewDbContextWith(order);
        var handler = new MarkItemReadyHandler(dbContext);
        var itemId = order.OrderItems.Single().Id.Value;

        await handler.Handle(
            new MarkItemReadyCommand(order.Id.Value, itemId),
            CancellationToken.None);

        var item = order.OrderItems.Single();
        item.PrepStatus.Should().Be(PrepStatus.Ready);
        item.PrepCompletedAt.Should().NotBeNull();
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelOrderHandler_FromPending_CancelsAndPersists()
    {
        var order = SeedOrder(OrderStatus.Pending);
        var dbContext = NewDbContextWith(order);
        var httpContextAccessor = NewHttpContextAccessor(Guid.NewGuid());
        var handler = new CancelOrderHandler(dbContext, httpContextAccessor);

        await handler.Handle(
            new CancelOrderCommand(order.Id.Value, "customer requested"),
            CancellationToken.None);

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("customer requested");
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelOrderHandler_MissingAuthContext_ThrowsUnauthorized()
    {
        var order = SeedOrder(OrderStatus.Pending);
        var dbContext = NewDbContextWith(order);
        var httpContextAccessor = Substitute.For<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((Microsoft.AspNetCore.Http.HttpContext?)null);
        var handler = new CancelOrderHandler(dbContext, httpContextAccessor);

        Func<Task> act = () => handler.Handle(
            new CancelOrderCommand(order.Id.Value, "reason"),
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static Microsoft.AspNetCore.Http.IHttpContextAccessor NewHttpContextAccessor(Guid staffUserId)
    {
        var claims = new[]
        {
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier,
                staffUserId.ToString())
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = principal };

        var accessor = Substitute.For<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }
}
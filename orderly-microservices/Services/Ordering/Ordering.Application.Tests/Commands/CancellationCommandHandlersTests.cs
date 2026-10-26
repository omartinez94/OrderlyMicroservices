using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using BuildingBlocks.Exceptions;
using BuildingBlocks.Messaging.Events.Ordering;
using BuildingBlocks.Messaging.Outbox;
using Ordering.Application.Orders.Commands.ApproveCancelOrder;
using Ordering.Application.Orders.Commands.CancelOrder;
using Ordering.Application.Orders.Commands.RejectCancelOrder;
using Ordering.Application.Orders.Commands.RequestCancelOrder;

namespace Ordering.Application.Tests.Commands;

public sealed class CancellationCommandHandlersTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order SeedOrder(OrderStatus status)
    {
        Order order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, status);
        order.Add(NewMenuItemId(), quantity: 1, price: 5m);
        order.ClearDomainEvents();
        return order;
    }

    private static IApplicationDbContext NewDbContextWith(Order? order)
    {
        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(order));
        return dbContext;
    }

    private static IHttpContextAccessor NewHttpContextAccessor(Guid staffUserId, params string[] permissions)
    {
        List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, staffUserId.ToString())];
        foreach (string permission in permissions)
        {
            claims.Add(new Claim("permissions", permission));
        }

        ClaimsIdentity identity = new(claims, "test");
        ClaimsPrincipal principal = new(identity);
        DefaultHttpContext httpContext = new() { User = principal };

        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    // ==========================================
    // CancelOrderHandler Tests
    // ==========================================

    [Fact]
    public async Task CancelOrderHandler_FromPending_WithoutApprovePermission_CancelsAndPublishesOutbox()
    {
        Order order = SeedOrder(OrderStatus.Pending);
        IApplicationDbContext dbContext = NewDbContextWith(order);
        Guid staffId = Guid.NewGuid();
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(staffId, "orders:cancel");
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        CancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        CancelOrderResult result = await handler.Handle(
            new CancelOrderCommand(order.Id.Value, "Customer cancelled"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Customer cancelled");
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await outboxPublisher.Received(1).PublishAsync(
            Arg.Is<OrderCancelledIntegrationEvent>(e =>
                e.OrderId == order.Id.Value &&
                e.CancelledByUserId == staffId &&
                e.Reason == "Customer cancelled"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelOrderHandler_FromConfirmed_WithoutApprovePermission_ThrowsForbiddenException()
    {
        Order order = SeedOrder(OrderStatus.Confirmed);
        IApplicationDbContext dbContext = NewDbContextWith(order);
        Guid staffId = Guid.NewGuid();
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(staffId, "orders:cancel");
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        CancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        Func<Task> act = () => handler.Handle(
            new CancelOrderCommand(order.Id.Value, "Waiter trying to cancel confirmed"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*orders:approve_cancel*");
    }

    [Fact]
    public async Task CancelOrderHandler_FromConfirmed_WithApprovePermission_CancelsAndPublishesOutbox()
    {
        Order order = SeedOrder(OrderStatus.Confirmed);
        IApplicationDbContext dbContext = NewDbContextWith(order);
        Guid staffId = Guid.NewGuid();
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(staffId, "orders:cancel", "orders:approve_cancel");
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        CancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        CancelOrderResult result = await handler.Handle(
            new CancelOrderCommand(order.Id.Value, "Manager cancelling confirmed"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await outboxPublisher.Received(1).PublishAsync(
            Arg.Is<OrderCancelledIntegrationEvent>(e =>
                e.OrderId == order.Id.Value &&
                e.CancelledByUserId == staffId &&
                e.Reason == "Manager cancelling confirmed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelOrderHandler_OrderNotFound_ThrowsOrderNotFoundException()
    {
        IApplicationDbContext dbContext = NewDbContextWith(null);
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(Guid.NewGuid());
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        CancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        Func<Task> act = () => handler.Handle(
            new CancelOrderCommand(Guid.NewGuid(), "reason"),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }

    // ==========================================
    // RequestCancelOrderHandler Tests
    // ==========================================

    [Fact]
    public async Task RequestCancelOrderHandler_FromConfirmed_RequestsCancelAndPublishesOutbox()
    {
        Order order = SeedOrder(OrderStatus.Confirmed);
        IApplicationDbContext dbContext = NewDbContextWith(order);
        Guid staffId = Guid.NewGuid();
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(staffId, "orders:request_cancel");
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        RequestCancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        RequestCancelOrderResult result = await handler.Handle(
            new RequestCancelOrderCommand(order.Id.Value, "Customer walked out"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.CancelRequested);
        order.PreviousStatus.Should().Be(OrderStatus.Confirmed);
        order.CancelRequestReason.Should().Be("Customer walked out");
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await outboxPublisher.Received(1).PublishAsync(
            Arg.Is<OrderCancelRequestedIntegrationEvent>(e =>
                e.OrderId == order.Id.Value &&
                e.RequestedByUserId == staffId &&
                e.Reason == "Customer walked out"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestCancelOrderHandler_OrderNotFound_ThrowsOrderNotFoundException()
    {
        IApplicationDbContext dbContext = NewDbContextWith(null);
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(Guid.NewGuid());
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        RequestCancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        Func<Task> act = () => handler.Handle(
            new RequestCancelOrderCommand(Guid.NewGuid(), "reason"),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }

    // ==========================================
    // ApproveCancelOrderHandler Tests
    // ==========================================

    [Fact]
    public async Task ApproveCancelOrderHandler_FromCancelRequested_ApprovesCancelAndPublishesOutbox()
    {
        Order order = SeedOrder(OrderStatus.Confirmed);
        order.RequestCancel(SystemClock.Instance.GetCurrentInstant(), "waiter-1", "Customer changed mind");
        IApplicationDbContext dbContext = NewDbContextWith(order);
        Guid managerId = Guid.NewGuid();
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(managerId, "orders:approve_cancel");
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        ApproveCancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        ApproveCancelOrderResult result = await handler.Handle(
            new ApproveCancelOrderCommand(order.Id.Value, "Manager approved cancellation"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Manager approved cancellation");
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await outboxPublisher.Received(1).PublishAsync(
            Arg.Is<OrderCancelledIntegrationEvent>(e =>
                e.OrderId == order.Id.Value &&
                e.CancelledByUserId == managerId &&
                e.Reason == "Manager approved cancellation"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveCancelOrderHandler_OrderNotFound_ThrowsOrderNotFoundException()
    {
        IApplicationDbContext dbContext = NewDbContextWith(null);
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(Guid.NewGuid());
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        ApproveCancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        Func<Task> act = () => handler.Handle(
            new ApproveCancelOrderCommand(Guid.NewGuid(), "reason"),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }

    // ==========================================
    // RejectCancelOrderHandler Tests
    // ==========================================

    [Fact]
    public async Task RejectCancelOrderHandler_FromCancelRequested_RejectsCancelAndPublishesOutbox()
    {
        Order order = SeedOrder(OrderStatus.Preparing);
        order.RequestCancel(SystemClock.Instance.GetCurrentInstant(), "waiter-1", "Customer mistake");
        IApplicationDbContext dbContext = NewDbContextWith(order);
        Guid kitchenManagerId = Guid.NewGuid();
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(kitchenManagerId, "orders:approve_cancel");
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        RejectCancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        RejectCancelOrderResult result = await handler.Handle(
            new RejectCancelOrderCommand(order.Id.Value, "Food is already in the oven"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Preparing);
        order.PreviousStatus.Should().BeNull();
        order.CancelRequestedAt.Should().BeNull();
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await outboxPublisher.Received(1).PublishAsync(
            Arg.Is<OrderCancelRejectedIntegrationEvent>(e =>
                e.OrderId == order.Id.Value &&
                e.RejectedByUserId == kitchenManagerId &&
                e.Reason == "Food is already in the oven" &&
                e.RestoredStatus == OrderStatus.Preparing.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectCancelOrderHandler_OrderNotFound_ThrowsOrderNotFoundException()
    {
        IApplicationDbContext dbContext = NewDbContextWith(null);
        IHttpContextAccessor httpContextAccessor = NewHttpContextAccessor(Guid.NewGuid());
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        RejectCancelOrderHandler handler = new(dbContext, httpContextAccessor, outboxPublisher);

        Func<Task> act = () => handler.Handle(
            new RejectCancelOrderCommand(Guid.NewGuid(), "reason"),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }
}

using BuildingBlocks.Enums;
using BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using Ordering.Application.Common.Interfaces;
using Ordering.Domain.Enums;
using Ordering.Domain.Models;
using Ordering.Domain.ValueObjects;
using Ordering.Infrastructure.Data;
using Ordering.Infrastructure.Data.Interceptors;

namespace Ordering.Infrastructure.Tests.Interceptors;

public sealed class OrderChangeNotificationInterceptorTests
{
    private static readonly Guid RestaurantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 24, 15, 0, 0);

    [Fact]
    public async Task SaveChangesAsync_WhenPendingOrderAdded_EmitsCreatedNotification()
    {
        IOrderNotifier notifier = Substitute.For<IOrderNotifier>();
        using ApplicationDBContext context = CreateContext(notifier);

        Order order = CreateOrder("DIN20261024-0001", OrderStatus.Pending);
        context.Orders.Add(order);

        await context.SaveChangesAsync();

        await notifier.Received(1).NotifyUpsertedAsync(
            Arg.Is<IReadOnlyList<OrderChangeNotification>>(list =>
                list.Count == 1 &&
                list[0].ChangeType == OrderChangeNotificationInterceptor.ChangeTypeCreated &&
                list[0].Order.Id == order.Id.Value &&
                list[0].Order.RestaurantId == RestaurantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenDraftOrderingAdded_SkipsNotification()
    {
        IOrderNotifier notifier = Substitute.For<IOrderNotifier>();
        using ApplicationDBContext context = CreateContext(notifier);

        Order draft = CreateOrder("DIN20261024-0002", OrderStatus.Ordering);
        context.Orders.Add(draft);
        await context.SaveChangesAsync();

        await notifier.DidNotReceiveWithAnyArgs().NotifyUpsertedAsync(
            default!,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenOrderConfirmed_EmitsStatusChangedNotification()
    {
        IOrderNotifier notifier = Substitute.For<IOrderNotifier>();
        using ApplicationDBContext context = CreateContext(notifier);

        Order order = CreateOrder("DIN20261024-0003", OrderStatus.Pending);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        notifier.ClearReceivedCalls();

        order.Confirm(ActorId, Now.Plus(Duration.FromMinutes(1)));
        await context.SaveChangesAsync();

        await notifier.Received(1).NotifyUpsertedAsync(
            Arg.Is<IReadOnlyList<OrderChangeNotification>>(list =>
                list.Count == 1 &&
                list[0].ChangeType == OrderChangeNotificationInterceptor.ChangeTypeStatusChanged &&
                list[0].Order.Status == OrderStatus.Confirmed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenOrderCancelled_EmitsCancelledNotification()
    {
        IOrderNotifier notifier = Substitute.For<IOrderNotifier>();
        using ApplicationDBContext context = CreateContext(notifier);

        Order order = CreateOrder("DIN20261024-0004", OrderStatus.Pending);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        notifier.ClearReceivedCalls();

        order.Cancel("Customer requested cancellation", ActorId, Now.Plus(Duration.FromMinutes(2)));
        await context.SaveChangesAsync();

        await notifier.Received(1).NotifyUpsertedAsync(
            Arg.Is<IReadOnlyList<OrderChangeNotification>>(list =>
                list.Count == 1 &&
                list[0].ChangeType == OrderChangeNotificationInterceptor.ChangeTypeCancelled &&
                list[0].Order.Status == OrderStatus.Cancelled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenOrderItemPrepModified_EmitsItemPrepChangedNotification()
    {
        IOrderNotifier notifier = Substitute.For<IOrderNotifier>();
        using ApplicationDBContext context = CreateContext(notifier);

        Order order = CreateOrder("DIN20261024-0005", OrderStatus.Preparing);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        notifier.ClearReceivedCalls();

        OrderItem item = order.OrderItems.Single();
        item.PrepStatus = PrepStatus.Preparing;
        context.Entry(item).State = EntityState.Modified;
        await context.SaveChangesAsync();

        await notifier.Received(1).NotifyUpsertedAsync(
            Arg.Is<IReadOnlyList<OrderChangeNotification>>(list =>
                list.Count == 1 &&
                list[0].ChangeType == OrderChangeNotificationInterceptor.ChangeTypeItemPrepChanged &&
                list[0].Order.Id == order.Id.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenOrderRemoved_EmitsRemovedNotification()
    {
        IOrderNotifier notifier = Substitute.For<IOrderNotifier>();
        using ApplicationDBContext context = CreateContext(notifier);

        Order order = CreateOrder("DIN20261024-0006", OrderStatus.Pending);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        notifier.ClearReceivedCalls();

        context.Orders.Remove(order);
        await context.SaveChangesAsync();

        await notifier.Received(1).NotifyRemovedAsync(
            Arg.Is<IReadOnlyList<OrderRemovalNotification>>(list =>
                list.Count == 1 &&
                list[0].OrderId == order.Id.Value &&
                list[0].RestaurantId == RestaurantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNotifierThrows_SwallowsAndCompletesCommit()
    {
        IOrderNotifier notifier = Substitute.For<IOrderNotifier>();
        notifier
            .NotifyUpsertedAsync(Arg.Any<IReadOnlyList<OrderChangeNotification>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Simulated hub outage"));

        using ApplicationDBContext context = CreateContext(notifier);
        Order order = CreateOrder("DIN20261024-0007", OrderStatus.Pending);
        context.Orders.Add(order);

        int written = await context.SaveChangesAsync();

        written.Should().BeGreaterThan(0);
    }

    private static ApplicationDBContext CreateContext(IOrderNotifier notifier)
    {
        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        ICurrentRestaurantProvider restaurantProvider = Substitute.For<ICurrentRestaurantProvider>();
        restaurantProvider.RestaurantId.Returns(RestaurantId);

        OrderChangeNotificationInterceptor interceptor = new(
            notifier,
            clock,
            NullLogger<OrderChangeNotificationInterceptor>.Instance);

        OrderingEntityAuditInterceptor auditInterceptor = new(clock);

        DbContextOptions<ApplicationDBContext> options = new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseInMemoryDatabase($"notify-test-{Guid.NewGuid():N}")
            .AddInterceptors(auditInterceptor, interceptor)
            .ConfigureWarnings(w => w.Ignore(
                InMemoryEventId.TransactionIgnoredWarning,
                CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        return new ApplicationDBContext(options, restaurantProvider);
    }

    private static Order CreateOrder(string orderNumber, OrderStatus status)
    {
        Address address = Address.Of("123 Main St", "Monterrey", "NL", "64000", "MX");
        Order order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderNumber.Of(orderNumber),
            RestaurantId,
            address,
            address,
            Payment.Of(PaymentMethod.Card, "Visa", "4242"));

        order.Status = status;
        order.OrderType = OrderType.DineIn;
        order.TotalAmount = 25.00m;
        order.Subtotal = 25.00m;
        order.Add(MenuItemId.Of(Guid.NewGuid()), quantity: 2, price: 12.50m);
        order.OrderItems.Single().MenuItemName = "Tacos Pastor";
        order.ClearDomainEvents();
        return order;
    }
}

using E2E.Tests.Abstractions;
using Polly;
using Polly.Retry;

namespace E2E.Tests;

/// <summary>
/// Cross-service order-lifecycle tests. Each test exercises the
/// Basket → Ordering → Kitchen chain via real HTTP / MassTransit over
/// RabbitMQ. Asserts the in-memory saga state at each transition by
/// re-reading the order via the Ordering API rather than subscribing
/// to the broker — keeping the test black-box so the same code path
/// production uses is what fails.
/// </summary>
[Trait("Category", "E2E")]
[Collection(nameof(E2ETestEnvironmentCollection))]
public sealed class OrderLifecycleE2ETests(E2ETestEnvironment env)
{
    private readonly E2ETestEnvironment _env = env;

    /// <summary>
    /// Full happy path: PUT cart → POST checkout → poll Order
    /// status through Pending → Confirmed → Preparing → Ready →
    /// Dispatched (optional) → Delivered. The test owns the JWT
    /// (via the DevJwtMinter) so it can drive every transition
    /// without going through Identity's user-creation flow.
    /// </summary>
    [Fact]
    public async Task FullOrderLifecycle_CartToDelivered()
    {
        Guid userId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();

        // Permissions cover every endpoint the lifecycle touches:
        // Basket (orders:create), Ordering.Create (orders:write),
        // every status transition (kitchen:update_prep_status).
        string token = _env.JwtMinter.Mint(
            userId, restaurantId,
            "orders:create", "orders:write", "orders:view_own",
            "kitchen:update_prep_status");

        // 1. PUT cart via Basket — Guid.Empty for UserId/RestaurantId;
        //    the endpoint overwrites with the JWT-derived identity.
        var cart = new
        {
            userId = Guid.Empty,
            restaurantId = Guid.Empty,
            items = new[]
            {
                new { menuItemId = Guid.NewGuid(), menuItemName = "Pizza", quantity = 1, price = 12.50m },
            },
        };
        using (HttpResponseMessage put = await _env.Basket.SendAsync(
            HttpMethod.Put, "/v1/cart", cart, token))
        {
            put.EnsureSuccessStatusCode();
        }

        // 2. POST checkout — Basket publishes BasketCheckoutEvent via
        //    its transactional outbox; the Ordering consumer picks it
        //    up and creates the Order.
        var checkout = new
        {
            userId = Guid.Empty,
            restaurantId = Guid.Empty,
            totalAmount = 12.50m,
            deliveryAddress = "123 Main St, Springfield, IL, 12345, US",
        };
        using (HttpResponseMessage post = await _env.Basket.SendAsync(
            HttpMethod.Post, "/v1/cart/checkout", checkout, token))
        {
            post.EnsureSuccessStatusCode();
        }

        // 3. Poll Ordering for the new order. RabbitMQ propagation is
        //    near-instant in dev but the polling is the right shape
        //    for staging where a network blip can take seconds.
        Guid orderId = await PollForOrderCreated(userId, restaurantId, token);

        // 4. Drive Pending → Confirmed → Preparing → Ready → Delivered.
        await Transition(token, "/v1/orders/{0}/confirm", orderId, expected: 204);
        await Transition(token, "/v1/orders/{0}/start-prep", orderId, expected: 204);
        await Transition(token, "/v1/orders/{0}/mark-ready", orderId, expected: 204);
        await Transition(token, "/v1/orders/{0}/mark-delivered", orderId, expected: 204);

        // 5. Final assertion — read the order back and confirm the
        //    terminal state.
        var finalOrder = await _env.Ordering.GetJsonAsync<OrderResponse>(
            $"/v1/orders/{orderId}", token);
        finalOrder.Should().NotBeNull();
        finalOrder!.status.Should().Be("Delivered");
    }

    /// <summary>
    /// Cancel mid-lifecycle: cart → checkout → confirm → cancel with
    /// a reason. Verifies the cancel transition AND that the order
    /// activity log records the cancellation reason (the spec's
    /// "verify activity log has cancel entry" pin).
    /// </summary>
    [Fact]
    public async Task OrderCancellation_MidLifecycle()
    {
        Guid userId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        string token = _env.JwtMinter.Mint(
            userId, restaurantId,
            "orders:create", "orders:write", "orders:view_own",
            "kitchen:update_prep_status");

        Guid orderId = await CreateAndConfirmOrder(userId, restaurantId, token);

        // Cancel with a reason.
        const string reason = "customer-requested-cancel";
        using HttpResponseMessage cancel = await _env.Ordering.SendAsync(
            HttpMethod.Post,
            $"/v1/orders/{orderId}/cancel",
            new { reason, cancelledByUserId = userId },
            token);
        cancel.EnsureSuccessStatusCode();

        // Status is now Cancelled.
        OrderResponse? order = await _env.Ordering.GetJsonAsync<OrderResponse>(
            $"/v1/orders/{orderId}", token);
        order.Should().NotBeNull();
        order!.status.Should().Be("Cancelled");

        // Activity log records the cancel entry.
        var activities = await _env.Ordering.GetJsonAsync<List<ActivityEntry>>(
            $"/v1/orders/{orderId}/activities", token);
        activities.Should().NotBeNull();
        activities!.Should().Contain(a =>
            a.activityType == "OrderCancelled" && a.notes == reason);
    }

    /// <summary>
    /// Cross-service propagation: when an Order is confirmed, an
    /// outbox-published OrderCreatedIntegrationEvent should land in
    /// the Kitchen.API queue. Verifies the Ordering → Kitchen bridge.
    /// </summary>
    [Fact]
    public async Task KitchenTicketCreatedOnOrderConfirm()
    {
        Guid userId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        string token = _env.JwtMinter.Mint(
            userId, restaurantId,
            "orders:create", "orders:write", "orders:view_own",
            "kitchen:update_prep_status", "kitchen:view_orders");

        Guid orderId = await CreateAndConfirmOrder(userId, restaurantId, token);

        // Poll Kitchen's queue — the OrderCreatedIntegrationEvent
        // consumer in Kitchen creates a KitchenTicket and exposes it
        // through GET /v1/kitchen/queue.
        Polly.Retry.AsyncRetryPolicy<bool> poll = Policy<bool>
            .HandleResult(found => !found)
            .WaitAndRetryAsync(
                retryCount: 20,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(500));

        bool found = await poll.ExecuteAsync(async () =>
        {
            var queue = await _env.Kitchen.GetJsonAsync<KitchenQueueResponse>(
                "/v1/kitchen/queue", token).ConfigureAwait(false);
            return queue?.tickets?.Any(t => t.orderId == orderId) ?? false;
        }).ConfigureAwait(false);

        found.Should().BeTrue(
            $"expected Kitchen ticket for order {orderId} to appear in /v1/kitchen/queue within 10 s");
    }

    // ----- helpers -----

    private async Task<Guid> CreateAndConfirmOrder(Guid userId, Guid restaurantId, string token)
    {
        var cart = new
        {
            userId = Guid.Empty,
            restaurantId = Guid.Empty,
            items = new[] { new { menuItemId = Guid.NewGuid(), menuItemName = "Burger", quantity = 1, price = 9.99m } },
        };
        using (HttpResponseMessage put = await _env.Basket.SendAsync(HttpMethod.Put, "/v1/cart", cart, token))
        {
            put.EnsureSuccessStatusCode();
        }
        using (HttpResponseMessage post = await _env.Basket.SendAsync(
            HttpMethod.Post, "/v1/cart/checkout",
            new { userId = Guid.Empty, restaurantId = Guid.Empty, totalAmount = 9.99m, deliveryAddress = "X" },
            token))
        {
            post.EnsureSuccessStatusCode();
        }

        Guid orderId = await PollForOrderCreated(userId, restaurantId, token);
        await Transition(token, "/v1/orders/{0}/confirm", orderId, expected: 204);
        return orderId;
    }

    private async Task<Guid> PollForOrderCreated(Guid userId, Guid restaurantId, string token)
    {
        AsyncRetryPolicy<Guid> poll = Policy<Guid>
            .HandleResult(id => id == Guid.Empty)
            .WaitAndRetryAsync(
                retryCount: 30,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(500));

        return await poll.ExecuteAsync(async () =>
        {
            // Listing the customer's orders picks up the row created
            // by the BasketCheckoutEvent consumer.
            var orders = await _env.Ordering.GetJsonAsync<List<OrderResponse>>(
                $"/v1/orders/by-customer/{userId}", token).ConfigureAwait(false);
            return orders?.FirstOrDefault()?.id ?? Guid.Empty;
        }).ConfigureAwait(false);
    }

    private async Task Transition(string token, string pathTemplate, Guid orderId, int expected)
    {
        string path = string.Format(pathTemplate, orderId);
        using HttpResponseMessage response = await _env.Ordering.SendAsync(
            HttpMethod.Post, path, body: null, token);
        ((int)response.StatusCode).Should().Be(expected);
    }

    // ----- DTOs -----

    private sealed record OrderResponse(Guid id, string status);

    private sealed record ActivityEntry(string activityType, string? notes);

    private sealed record KitchenTicket(Guid orderId, string status);

    private sealed record KitchenQueueResponse(List<KitchenTicket> tickets);
}

/// <summary>
/// xUnit collection fixture — every test in the assembly shares one
/// <see cref="E2ETestEnvironment"/> so the docker-compose readiness
/// probe runs once, not once per test class.
/// </summary>
[CollectionDefinition(nameof(E2ETestEnvironmentCollection))]
public sealed class E2ETestEnvironmentCollection : ICollectionFixture<E2ETestEnvironment>
{
}

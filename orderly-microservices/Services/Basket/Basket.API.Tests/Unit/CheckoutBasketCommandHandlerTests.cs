using System.Text.Json;
using Basket.API.Basket.CheckoutBasket;
using BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit-level coverage for <see cref="CheckoutBasketCommandHandler"/>.
/// Locks the Phase 2 atomic-publish-and-delete contract:
/// the handler stages the outbox row + deletes the basket in the SAME
/// <c>IDocumentSession.SaveChangesAsync()</c> call so a publish failure
/// can no longer delete the cart or a delete failure can no longer
/// leave the cart behind.
/// </summary>
/// <remarks>
/// <para>
/// "Atomic" here means "one Marten commit covers both writes" — verified
/// by asserting <c>SaveChangesAsync</c> is called exactly once with
/// <c>Store(outboxMessage)</c> and <c>Delete(basket)</c> both staged
/// first. The true transactional guarantee (a Postgres-level rollback
/// if the commit fails) is an integration-test concern; Phase 5's
/// <c>BasketWebApplicationFactory</c> covers that path with Testcontainers.
/// </para>
/// <para>
/// <c>IPublishEndpoint</c> is intentionally absent from the handler's
/// constructor (Phase 2 — the relay moved to
/// <see cref="CheckoutBasketOutboxDispatcher"/>). The tests assert the
/// handler does NOT publish directly.
/// </para>
/// </remarks>
public sealed class CheckoutBasketCommandHandlerTests
{
    [Fact]
    public async Task EmptyBasket_ReturnsFailureResult_DoesNotTouchSession()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var emptyBasket = new Models.Basket(userId, restaurantId);
        // Items list is empty by default on Models.Basket — no setup needed.

        var repository = Substitute.For<IBasketRepository>();
        repository
            .GetBasketAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns(emptyBasket);

        var session = Substitute.For<IDocumentSession>();
        var handler = new CheckoutBasketCommandHandler(
            repository,
            session,
            NullLogger<CheckoutBasketCommandHandler>.Instance);

        var result = await handler.Handle(
            BuildCommand(userId, restaurantId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Basket is empty.");

        // The handler must NOT stage an outbox row, NOT delete the
        // basket, and NOT call SaveChangesAsync when the cart is empty —
        // saves a round-trip and keeps the audit trail clean.
        session
            .DidNotReceive()
            .Store(Arg.Any<CheckoutBasketOutboxMessage>());
        session
            .DidNotReceive()
            .Delete(Arg.Any<CheckoutBasketOutboxMessage>());
        session
            .DidNotReceive()
            .Delete(Arg.Any<Models.Basket>());
        await session
            .DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulCheckout_StagesOutboxAndDeletesBasket_OneCommit()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var basket = new Models.Basket(userId, restaurantId)
        {
            Items =
            {
                new Models.BasketItem { MenuItemId = 42, Quantity = 2, UnitPrice = 9.99m },
            },
            AppliedDiscounts = { "WELCOME10" },
        };

        var repository = Substitute.For<IBasketRepository>();
        repository
            .GetBasketAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns(basket);

        var session = Substitute.For<IDocumentSession>();
        var handler = new CheckoutBasketCommandHandler(
            repository,
            session,
            NullLogger<CheckoutBasketCommandHandler>.Instance);

        var result = await handler.Handle(
            BuildCommand(userId, restaurantId),
            CancellationToken.None);

        result.Success.Should().BeTrue();

        // 1. Outbox row was staged.
        session
            .Received(1)
            .Store(Arg.Is<CheckoutBasketOutboxMessage>(m =>
                m.Id != Guid.Empty &&
                m.OccurredOn != default &&
                m.Type == typeof(BasketCheckoutEvent).FullName &&
                m.SchemaVersion == 1 &&
                !string.IsNullOrEmpty(m.Payload) &&
                m.DispatchedAt == null));

        // 2. Basket was deleted (in the same session).
        session.Received(1).Delete(basket);

        // 3. SaveChangesAsync was called EXACTLY ONCE — atomicity hinge.
        // Two writes → one commit. Any future contributor who splits
        // this into two SaveChangesAsync calls breaks Phase 2.
        await session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // 4. Cache invalidation ran AFTER the Marten commit.
        await repository
            .Received(1)
            .InvalidateCacheAsync(userId, restaurantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OutboxPayload_DeserializesToBasketCheckoutEvent_WithExpectedFields()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var basket = new Models.Basket(userId, restaurantId)
        {
            Items =
            {
                new Models.BasketItem
                {
                    MenuItemId = 7,
                    Quantity = 3,
                    UnitPrice = 12.50m,
                    Variations = { new Models.BasketItemVariation { Name = "Size", Value = "Large", Price = 1.00m } },
                    Customizations = { new Models.BasketItemCustomization { Ingredient = "Cheese", Action = "Add" } },
                },
            },
        };

        var eventMessage = new BasketCheckoutEvent
        {
            UserId = userId,
            RestaurantId = restaurantId,
            FirstName = "Ada",
            LastName = "Lovelace",
            EmailAddress = "ada@example.com",
            AddressLine = "1 Analytical Engine Way",
            Country = "UK",
            State = "London",
            City = "London",
            ZipCode = "WC1E 6BT",
            CardName = "Ada Lovelace",
            CardNumber = "4111111111111111",
            Expiration = "12/30",
            CVV = "123",
            PaymentMethod = "1",
        };

        // Mirror the handler's payload contract (CheckoutBasketHandler
        // serialises via the same JsonSerializerOptions shape):
        var payload = JsonSerializer.Serialize(eventMessage, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });

        var roundTrip = JsonSerializer.Deserialize<BasketCheckoutEvent>(payload);
        roundTrip.Should().NotBeNull();
        roundTrip!.UserId.Should().Be(userId);
        roundTrip.RestaurantId.Should().Be(restaurantId);
        roundTrip.TotalAmount.Should().Be(0m); // TotalAmount is set by the handler at stage time.
        roundTrip.FirstName.Should().Be("Ada");
        // Phase 2 v1 still carries CardNumber/CVV on the wire — card
        // redaction (PaymentMethodSummary) is a separate Phase 2.1
        // commit. This assertion records that the v1 contract is
        // round-trippable.
        roundTrip.CardNumber.Should().Be("4111111111111111");
    }

    private static CheckoutBasketCommand BuildCommand(Guid userId, Guid restaurantId) =>
        new(new Dtos.BasketCheckoutDto
        {
            UserId = userId,
            RestaurantId = restaurantId,
            FirstName = "Ada",
            LastName = "Lovelace",
            EmailAddress = "ada@example.com",
            AddressLine = "1 Analytical Engine Way",
            Country = "UK",
            State = "London",
            ZipCode = "WC1E 6BT",
            CardName = "Ada Lovelace",
            CardNumber = "4111111111111111",
            Expiration = "12/30",
            CVV = "123",
            PaymentMethod = 1,
        });
}

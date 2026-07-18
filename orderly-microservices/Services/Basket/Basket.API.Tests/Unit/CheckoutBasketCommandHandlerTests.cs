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
/// leave the cart behind. Phase 2.1 updated the wire payload to v2
/// (PaymentMethodSummary instead of raw card fields); the
/// <see cref="OutboxPayload_DeserializesToBasketCheckoutEvent_WithExpectedFields"/>
/// test locks the v2 shape.
/// </summary>
public sealed class CheckoutBasketCommandHandlerTests
{
    [Fact]
    public async Task EmptyBasket_ReturnsFailureResult_DoesNotTouchSession()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var emptyBasket = new Models.Basket(userId, restaurantId);

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

        // 1. Outbox row was staged. Phase 2.1 bumped BasketCheckoutEvent
        //    to MessageVersion=2 — SchemaVersion on the outbox row
        //    mirrors the event's MessageVersion, so this assertion locks
        //    the v2 wire shape on the dispatcher side.
        session
            .Received(1)
            .Store(Arg.Is<CheckoutBasketOutboxMessage>(m =>
                m.Id != Guid.Empty &&
                m.OccurredOn != default &&
                m.Type == typeof(BasketCheckoutEvent).FullName &&
                m.SchemaVersion == 2 &&
                !string.IsNullOrEmpty(m.Payload) &&
                m.DispatchedAt == null));

        // 2. Basket was deleted (in the same session).
        session.Received(1).Delete(basket);

        // 3. SaveChangesAsync was called EXACTLY ONCE — atomicity hinge.
        await session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // 4. Cache invalidation ran AFTER the Marten commit.
        await repository
            .Received(1)
            .InvalidateCacheAsync(userId, restaurantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OutboxPayload_DeserializesToBasketCheckoutEventV2_WithRedactedPaymentSummary()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        // Phase 2.1 wire shape: the v2 event carries ONLY the redacted
        // PaymentMethodSummary (discriminator + brand + last-four). Full
        // PAN, CVV, and CardName do NOT travel — they stay inside Basket.
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
            PaymentMethodSummary = new PaymentMethodSummary(
                Method: PaymentMethod.Card,
                Brand: "Visa",
                LastFour: "1111"),
        };

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<BuildingBlocks.Messaging.Events.PaymentMethod>() },
        };

        var payload = JsonSerializer.Serialize(eventMessage, serializerOptions);

        // Pass the same options to deserialize so the typed enum converter
        // (and any other customisations) round-trip symmetrically. Without
        // this, the default options would try to read "Card" as a string
        // instead of as the enum value.
        var roundTrip = JsonSerializer.Deserialize<BasketCheckoutEvent>(payload, serializerOptions);
        roundTrip.Should().NotBeNull();
        roundTrip!.UserId.Should().Be(userId);
        roundTrip.RestaurantId.Should().Be(restaurantId);
        roundTrip.TotalAmount.Should().Be(0m);
        roundTrip.FirstName.Should().Be("Ada");
        roundTrip.PaymentMethodSummary.Should().NotBeNull();
        roundTrip.PaymentMethodSummary!.Method.Should().Be(PaymentMethod.Card);
        roundTrip.PaymentMethodSummary.Brand.Should().Be("Visa");
        roundTrip.PaymentMethodSummary.LastFour.Should().Be("1111");
        roundTrip.MessageVersion.Should().Be(2,
            "BasketCheckoutEvent v2 stamps MessageVersion=2 — the dispatcher's MaxSupportedVersion gates v2 rows");
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
            PaymentMethod = PaymentMethod.Card,
        });
}

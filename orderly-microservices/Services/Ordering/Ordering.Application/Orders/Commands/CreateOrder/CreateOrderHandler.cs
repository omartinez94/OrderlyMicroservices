namespace Ordering.Application.Orders.Commands.CreateOrder;

using BuildingBlocks.Exceptions;
using Microsoft.EntityFrameworkCore;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Exceptions;
using Ordering.Application.Extensions;

public class CreateOrderHandler(
    IApplicationDbContext dbContext,
    IOrderNumberGenerator orderNumberGenerator,
    IOutboxPublisher outboxPublisher,
    ICatalogPricingClient catalogPricingClient)
    : ICommandHandler<CreateOrderCommand, CreateOrderResult>
{
    public async Task<CreateOrderResult> Handle(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        OrderDto dto = command.Order;

        // 1. Idempotency replay check
        if (command.IdempotencyKey.HasValue)
        {
            Order? existingOrder = await dbContext.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Activities)
                .FirstOrDefaultAsync(o => o.RestaurantId == dto.RestaurantId && o.IdempotencyKey == command.IdempotencyKey.Value, cancellationToken);

            if (existingOrder is not null)
            {
                return new CreateOrderResult(existingOrder.Id.Value, existingOrder.ToOrderDto(), IsReplay: true);
            }
        }

        // 2. Dine-In Table and Seat Validation
        if (dto.OrderType == OrderType.DineIn)
        {
            if (!dto.TableId.HasValue || dto.TableId.Value == Guid.Empty)
            {
                throw new BadRequestException("TableId is required for DineIn orders.");
            }

            CatalogTableDto? table = await catalogPricingClient.GetTableAsync(dto.RestaurantId, dto.TableId.Value, cancellationToken);
            if (table is null || table.RestaurantId != dto.RestaurantId)
            {
                throw new TableNotFoundUnprocessableException(dto.TableId.Value);
            }

            if (table.Capacity > 0)
            {
                foreach (OrderItemDto item in dto.OrderItems)
                {
                    if (item.SeatNumber > table.Capacity)
                    {
                        throw new SeatNumberExceedsCapacityUnprocessableException(table.Id, item.SeatNumber, table.Capacity);
                    }
                }
            }
        }

        // 3. Catalog Menu Snapshot, Pricing & Availability Validation
        CatalogMenuSnapshot snapshot = await catalogPricingClient.GetMenuSnapshotAsync(dto.RestaurantId, cancellationToken);
        Dictionary<Guid, CatalogMenuItemDto> catalogItems = snapshot.Items.ToDictionary(i => i.Id);

        List<Guid> missingItemIds = [];
        List<Guid> unavailableItemIds = [];

        foreach (OrderItemDto item in dto.OrderItems)
        {
            if (!catalogItems.TryGetValue(item.MenuItemId, out CatalogMenuItemDto? catalogItem))
            {
                missingItemIds.Add(item.MenuItemId);
                continue;
            }

            bool isSoldOut = string.Equals(catalogItem.AvailabilityStatus, "SoldOut", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(catalogItem.AvailabilityStatus, "Unavailable", StringComparison.OrdinalIgnoreCase) ||
                             !catalogItem.IsAvailable;

            if (isSoldOut)
            {
                unavailableItemIds.Add(item.MenuItemId);
            }
        }

        if (missingItemIds.Count > 0)
        {
            throw new MenuItemNotFoundUnprocessableException(missingItemIds);
        }

        if (unavailableItemIds.Count > 0)
        {
            throw new MenuItemUnavailableConflictException(unavailableItemIds);
        }

        // 4. Overwrite item prices and recompute bill financials server-side
        decimal computedSubtotal = 0m;
        List<(OrderItemDto Item, decimal AuthoritativeUnitPrice, int? CourseNumber)> preparedItems = [];

        foreach (OrderItemDto item in dto.OrderItems)
        {
            CatalogMenuItemDto catalogItem = catalogItems[item.MenuItemId];
            decimal authoritativeUnitPrice = catalogItem.PromoPrice ?? catalogItem.BasePrice;

            if (item.SelectedVariations is not null && item.SelectedVariations.Count > 0)
            {
                Dictionary<string, CatalogMenuItemVariationDto> variationByName = catalogItem.Variations
                    .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                foreach (KitchenOrderItemVariation selectedVar in item.SelectedVariations)
                {
                    if (variationByName.TryGetValue(selectedVar.Name, out CatalogMenuItemVariationDto? catVar))
                    {
                        authoritativeUnitPrice += catVar.PriceModifier;
                    }
                }
            }

            int? courseNum = item.CourseNumber ?? catalogItem.DefaultCourseNumber;
            computedSubtotal += authoritativeUnitPrice * item.Quantity;
            preparedItems.Add((item, authoritativeUnitPrice, courseNum));
        }

        decimal taxRate = dto.TaxRate;
        decimal computedTaxAmount = Math.Round(computedSubtotal * taxRate, 2, MidpointRounding.AwayFromZero);
        decimal discountAmount = Math.Min(dto.DiscountAmount, computedSubtotal);
        decimal computedTotalAmount = Math.Max(0m, computedSubtotal + computedTaxAmount - discountAmount);

        // 5. Build addresses, customer, payment
        Address billingAddress = dto.BillingAddress is not null
            ? Address.Of(
                dto.BillingAddress.Street,
                dto.BillingAddress.City,
                dto.BillingAddress.State,
                dto.BillingAddress.ZipCode,
                dto.BillingAddress.Country)
            : Address.None;

        Address deliveryAddress = dto.DeliveryAddress is not null
            ? Address.Of(
                dto.DeliveryAddress.Street,
                dto.DeliveryAddress.City,
                dto.DeliveryAddress.State,
                dto.DeliveryAddress.ZipCode,
                dto.DeliveryAddress.Country)
            : Address.None;

        Payment payment = dto.Payment is not null
            ? Payment.Of(
                dto.Payment.Method,
                dto.Payment.Brand,
                dto.Payment.LastFour)
            : Payment.Pending;

        CustomerId customerId;
        if (dto.CustomerId == Guid.Empty && dto.OrderType != OrderType.Delivery)
        {
            Guid walkInId = WalkInCustomer.IdFor(dto.RestaurantId);
            customerId = CustomerId.Of(walkInId);

            Customer? existingCustomer = await dbContext.Customers.FindAsync([customerId], cancellationToken);
            if (existingCustomer is null)
            {
                Customer walkInCustomer = Customer.Create(
                    customerId,
                    $"walkin-{dto.RestaurantId:N}@orderly.internal",
                    "Walk-in Customer",
                    string.Empty);
                dbContext.Customers.Add(walkInCustomer);
            }
        }
        else
        {
            customerId = CustomerId.Of(dto.CustomerId);
        }

        OrderId orderId = OrderId.Of(Guid.NewGuid());

        OrderNumber orderNumber;
        if (string.IsNullOrWhiteSpace(dto.OrderNumber) || dto.OrderNumber.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            orderNumber = await orderNumberGenerator.GenerateAsync(dto.RestaurantId, dto.OrderType, cancellationToken);
        }
        else
        {
            orderNumber = OrderNumber.Of(dto.OrderNumber);
        }

        Order order = Order.Create(
            orderId,
            customerId,
            orderNumber,
            dto.RestaurantId,
            billingAddress,
            deliveryAddress,
            payment);

        order.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency;
        order.OrderType = dto.OrderType;
        order.Notes = dto.Notes ?? string.Empty;
        order.DeliveryNotes = dto.DeliveryNotes ?? string.Empty;
        order.EstimatedPrepTimeMinutes = dto.EstimatedPrepTimeMinutes;
        order.RequiresAdminApproval = dto.RequiresAdminApproval;
        order.TableId = dto.TableId;
        order.CreatedByUserId = dto.CreatedByUserId;
        order.IdempotencyKey = command.IdempotencyKey;

        // Authoritative bill calculations
        order.Subtotal = computedSubtotal;
        order.TaxAmount = computedTaxAmount;
        order.TaxRate = taxRate;
        order.DiscountAmount = discountAmount;
        order.DiscountCode = dto.DiscountCode ?? string.Empty;
        order.TotalAmount = computedTotalAmount;

        // Add items with authoritative prices & course numbers
        foreach ((OrderItemDto item, decimal authoritativeUnitPrice, int? courseNum) in preparedItems)
        {
            CatalogMenuItemDto catalogItem = catalogItems[item.MenuItemId];
            string itemName = !string.IsNullOrWhiteSpace(item.MenuItemName) ? item.MenuItemName : catalogItem.Name;

            order.Add(
                MenuItemId.Of(item.MenuItemId),
                item.Quantity,
                authoritativeUnitPrice,
                itemName,
                item.SpecialInstructions,
                item.Customizations,
                item.SelectedVariations,
                item.SeatNumber,
                courseNum);
        }

        order.RecordActivity(
            Ordering.Domain.Enums.OrderActivityType.OrderCreated,
            actorUserId: dto.CreatedByUserId,
            occurredAt: SystemClock.Instance.GetCurrentInstant());

        dbContext.Orders.Add(order);

        OrderCreatedIntegrationEvent integrationEvent = order.ToOrderCreatedIntegrationEvent();
        await outboxPublisher.PublishAsync(integrationEvent, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateOrderResult(orderId.Value, order.ToOrderDto(), IsReplay: false);
    }
}
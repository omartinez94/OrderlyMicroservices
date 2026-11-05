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

        CreateOrderResult? replayResult = await CheckIdempotencyAsync(command, cancellationToken);
        if (replayResult is not null)
        {
            return replayResult;
        }

        await ValidateDineInTableAsync(dto, cancellationToken);

        Dictionary<Guid, CatalogMenuItemDto> catalogItems = await ValidateAndFetchCatalogItemsAsync(dto, cancellationToken);

        (decimal computedSubtotal, decimal computedTaxAmount, decimal discountAmount, decimal computedTotalAmount, List<(OrderItemDto Item, decimal AuthoritativeUnitPrice, int? CourseNumber)> preparedItems) =
            ComputeFinancials(dto, catalogItems);

        Address billingAddress = BuildBillingAddress(dto);
        Address deliveryAddress = BuildDeliveryAddress(dto);
        Payment payment = BuildPayment(dto);
        CustomerId customerId = await ResolveCustomerIdAsync(dto, cancellationToken);
        OrderNumber orderNumber = await ResolveOrderNumberAsync(dto, cancellationToken);

        OrderId orderId = OrderId.Of(Guid.NewGuid());
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

        order.Subtotal = computedSubtotal;
        order.TaxAmount = computedTaxAmount;
        order.TaxRate = dto.TaxRate;
        order.DiscountAmount = discountAmount;
        order.DiscountCode = dto.DiscountCode ?? string.Empty;
        order.TotalAmount = computedTotalAmount;

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

    private async Task<CreateOrderResult?> CheckIdempotencyAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.IdempotencyKey.HasValue)
        {
            return null;
        }

        Order? existingOrder = await dbContext.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Activities)
            .FirstOrDefaultAsync(o => o.RestaurantId == command.Order.RestaurantId && o.IdempotencyKey == command.IdempotencyKey.Value, cancellationToken);

        if (existingOrder is null)
        {
            return null;
        }

        return new CreateOrderResult(existingOrder.Id.Value, existingOrder.ToOrderDto(), IsReplay: true);
    }

    private async Task ValidateDineInTableAsync(
        OrderDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.OrderType != OrderType.DineIn)
        {
            return;
        }

        if (!dto.TableId.HasValue || dto.TableId.Value == Guid.Empty)
        {
            throw new BadRequestException("TableId is required for DineIn orders.");
        }

        CatalogTableDto? table = await catalogPricingClient.GetTableAsync(dto.RestaurantId, dto.TableId.Value, cancellationToken);
        if (table is null || table.RestaurantId != dto.RestaurantId)
        {
            throw new TableNotFoundUnprocessableException(dto.TableId.Value);
        }

        if (table.Capacity <= 0)
        {
            return;
        }

        foreach (OrderItemDto item in dto.OrderItems)
        {
            if (item.SeatNumber > table.Capacity)
            {
                throw new SeatNumberExceedsCapacityUnprocessableException(table.Id, item.SeatNumber, table.Capacity);
            }
        }
    }

    private async Task<Dictionary<Guid, CatalogMenuItemDto>> ValidateAndFetchCatalogItemsAsync(
        OrderDto dto,
        CancellationToken cancellationToken)
    {
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

        return catalogItems;
    }

    private static (decimal Subtotal, decimal TaxAmount, decimal DiscountAmount, decimal TotalAmount, List<(OrderItemDto Item, decimal AuthoritativeUnitPrice, int? CourseNumber)> Items)
        ComputeFinancials(OrderDto dto, Dictionary<Guid, CatalogMenuItemDto> catalogItems)
    {
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

        decimal computedTaxAmount = Math.Round(computedSubtotal * dto.TaxRate, 2, MidpointRounding.AwayFromZero);
        decimal discountAmount = Math.Min(dto.DiscountAmount, computedSubtotal);
        decimal computedTotalAmount = Math.Max(0m, computedSubtotal + computedTaxAmount - discountAmount);

        return (computedSubtotal, computedTaxAmount, discountAmount, computedTotalAmount, preparedItems);
    }

    private static Address BuildBillingAddress(OrderDto dto)
    {
        if (dto.BillingAddress is null)
        {
            return Address.None;
        }

        return Address.Of(
            dto.BillingAddress.Street,
            dto.BillingAddress.City,
            dto.BillingAddress.State,
            dto.BillingAddress.ZipCode,
            dto.BillingAddress.Country);
    }

    private static Address BuildDeliveryAddress(OrderDto dto)
    {
        if (dto.DeliveryAddress is null)
        {
            return Address.None;
        }

        return Address.Of(
            dto.DeliveryAddress.Street,
            dto.DeliveryAddress.City,
            dto.DeliveryAddress.State,
            dto.DeliveryAddress.ZipCode,
            dto.DeliveryAddress.Country);
    }

    private static Payment BuildPayment(OrderDto dto)
    {
        if (dto.Payment is null)
        {
            return Payment.Pending;
        }

        return Payment.Of(
            dto.Payment.Method,
            dto.Payment.Brand,
            dto.Payment.LastFour);
    }

    private async Task<CustomerId> ResolveCustomerIdAsync(OrderDto dto, CancellationToken cancellationToken)
    {
        if (dto.CustomerId != Guid.Empty || dto.OrderType == OrderType.Delivery)
        {
            return CustomerId.Of(dto.CustomerId);
        }

        Guid walkInId = WalkInCustomer.IdFor(dto.RestaurantId);
        CustomerId customerId = CustomerId.Of(walkInId);

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

        return customerId;
    }

    private async Task<OrderNumber> ResolveOrderNumberAsync(OrderDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.OrderNumber) || dto.OrderNumber.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return await orderNumberGenerator.GenerateAsync(dto.RestaurantId, dto.OrderType, cancellationToken);
        }

        return OrderNumber.Of(dto.OrderNumber);
    }
}

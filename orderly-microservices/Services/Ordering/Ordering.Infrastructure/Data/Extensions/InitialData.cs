namespace Ordering.Infrastructure.Data.Extensions;

public class InitialData
{
    public static IEnumerable<Customer> Customers =>
        [
            Customer.Create(
                CustomerId.Of(new Guid("58c49479-ec65-4de2-86e7-033c546291aa")),
                "john.doe@example.com",
                "John Doe",
                "+1234567890",
                Address.Of("123 Main St", "Anytown", "NY", "12345", "USA")
            ),
            Customer.Create(
                CustomerId.Of(new Guid("11111111-1111-1111-1111-111111111111")),
                "jane.smith@example.com",
                "Jane Smith",
                "+0987654321",
                Address.Of("456 Elm St", "Othertown", "CA", "54321", "USA")
            )
        ];

    public static IEnumerable<MenuItem> MenuItems =>
        [
            MenuItem.Create(
                MenuItemId.Of(new Guid("22222222-2222-2222-2222-222222222222")),
                "Cheeseburger",
                10.99m
            ),
            MenuItem.Create(
                MenuItemId.Of(new Guid("33333333-3333-3333-3333-333333333333")),
                "Fries",
                3.99m
            )
        ];

    public static IEnumerable<Order> Orders 
    {
        get 
        {
            var order1 = Order.Create(
                OrderId.Of(new Guid("44444444-4444-4444-4444-444444444444")),
                CustomerId.Of(new Guid("58c49479-ec65-4de2-86e7-033c546291aa")),
                OrderNumber.Of("ORD-0001"),
                Guid.NewGuid(),
                Address.Of("123 Main St", "Anytown", "NY", "12345", "USA"),
                Address.Of("123 Main St", "Anytown", "NY", "12345", "USA"),
                Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "3456")
            );
            order1.Add(MenuItemId.Of(new Guid("22222222-2222-2222-2222-222222222222")), 2, 10.99m);
            order1.Add(MenuItemId.Of(new Guid("33333333-3333-3333-3333-333333333333")), 1, 3.99m);

            var order2 = Order.Create(
                OrderId.Of(new Guid("55555555-5555-5555-5555-555555555555")),
                CustomerId.Of(new Guid("11111111-1111-1111-1111-111111111111")),
                OrderNumber.Of("ORD-0002"),
                Guid.NewGuid(),
                Address.Of("456 Elm St", "Othertown", "CA", "54321", "USA"),
                Address.Of("456 Elm St", "Othertown", "CA", "54321", "USA"),
                Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Mastercard", "7654")
            );
            order2.Add(MenuItemId.Of(new Guid("22222222-2222-2222-2222-222222222222")), 1, 10.99m);

            var order3 = Order.Create(
                OrderId.Of(new Guid("66666666-6666-6666-6666-666666666666")),
                CustomerId.Of(new Guid("58c49479-ec65-4de2-86e7-033c546291aa")),
                OrderNumber.Of("ORD-0003"),
                Guid.NewGuid(),
                Address.Of("123 Main St", "Anytown", "NY", "12345", "USA"),
                Address.Of("123 Main St", "Anytown", "NY", "12345", "USA"),
                Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "3456")
            );
            order3.Add(MenuItemId.Of(new Guid("33333333-3333-3333-3333-333333333333")), 2, 3.99m);

            var order4 = Order.Create(
                OrderId.Of(new Guid("77777777-7777-7777-7777-777777777777")),
                CustomerId.Of(new Guid("11111111-1111-1111-1111-111111111111")),
                OrderNumber.Of("ORD-0004"),
                Guid.NewGuid(),
                Address.Of("456 Elm St", "Othertown", "CA", "54321", "USA"),
                Address.Of("456 Elm St", "Othertown", "CA", "54321", "USA"),
                Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Mastercard", "7654")
            );
            order4.Add(MenuItemId.Of(new Guid("22222222-2222-2222-2222-222222222222")), 2, 10.99m);
            order4.Add(MenuItemId.Of(new Guid("33333333-3333-3333-3333-333333333333")), 2, 3.99m);

            return [order1, order2, order3, order4];
        }
    }

    public static IEnumerable<OrderBill> OrderBills =>
        [
            OrderBill.Create(
                new Guid("44444444-4444-4444-4444-444444444444"),
                1,
                25.97m,
                2.50m,
                28.47m
            ),
            OrderBill.Create(
                new Guid("55555555-5555-5555-5555-555555555555"),
                2,
                10.99m,
                1.00m,
                11.99m
            ),
            OrderBill.Create(
                new Guid("66666666-6666-6666-6666-666666666666"),
                3,
                7.98m,
                0.80m,
                8.78m
            ),
            OrderBill.Create(
                new Guid("77777777-7777-7777-7777-777777777777"),
                4,
                29.96m,
                3.00m,
                32.96m
            )
        ];
}

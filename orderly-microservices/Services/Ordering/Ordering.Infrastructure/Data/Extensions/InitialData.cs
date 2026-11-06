namespace Ordering.Infrastructure.Data.Extensions;

public class InitialData
{
    public static IEnumerable<Customer> Customers =>
        [
            Customer.Create(
                CustomerId.Of(new Guid("58c49479-ec65-4de2-86e7-033c546291aa")),
                "andrea.morales@gmail.com",
                "Andrea Morales",
                "+525512345678",
                Address.Of("Colima 180", "Ciudad de México", "CDMX", "06700", "México")
            ),
            Customer.Create(
                CustomerId.Of(new Guid("11111111-1111-1111-1111-111111111111")),
                "fernando.castillo@hotmail.com",
                "Fernando Castillo",
                "+528187654321",
                Address.Of("Av. Vasconcelos 400", "San Pedro Garza García", "NL", "66220", "México")
            )
        ];

    public static IEnumerable<MenuItem> MenuItems =>
        [
            MenuItem.Create(
                MenuItemId.Of(new Guid("22222222-2222-2222-2222-222222222222")),
                "Tacos al Pastor en Maíz Nixtamalizado",
                195.00m
            ),
            MenuItem.Create(
                MenuItemId.Of(new Guid("33333333-3333-3333-3333-333333333333")),
                "Guacamole Artesanal con Totopos",
                175.00m
            )
        ];

    public static IEnumerable<Order> Orders
    {
        get
        {
            Order? order1 = Order.Create(
                OrderId.Of(new Guid("44444444-4444-4444-4444-444444444444")),
                CustomerId.Of(new Guid("58c49479-ec65-4de2-86e7-033c546291aa")),
                OrderNumber.Of("ORD-0001"),
                Guid.NewGuid(),
                Address.Of("Colima 180", "Ciudad de México", "CDMX", "06700", "México"),
                Address.Of("Colima 180", "Ciudad de México", "CDMX", "06700", "México"),
                Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "3456")
            );
            order1.Add(MenuItemId.Of(new Guid("22222222-2222-2222-2222-222222222222")), 2, 195.00m);
            order1.Add(MenuItemId.Of(new Guid("33333333-3333-3333-3333-333333333333")), 1, 175.00m);

            Order? order2 = Order.Create(
                OrderId.Of(new Guid("55555555-5555-5555-5555-555555555555")),
                CustomerId.Of(new Guid("11111111-1111-1111-1111-111111111111")),
                OrderNumber.Of("ORD-0002"),
                Guid.NewGuid(),
                Address.Of("Av. Vasconcelos 400", "San Pedro Garza García", "NL", "66220", "México"),
                Address.Of("Av. Vasconcelos 400", "San Pedro Garza García", "NL", "66220", "México"),
                Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Mastercard", "7654")
            );
            order2.Add(MenuItemId.Of(new Guid("22222222-2222-2222-2222-222222222222")), 1, 195.00m);

            Order? order3 = Order.Create(
                OrderId.Of(new Guid("66666666-6666-6666-6666-666666666666")),
                CustomerId.Of(new Guid("58c49479-ec65-4de2-86e7-033c546291aa")),
                OrderNumber.Of("ORD-0003"),
                Guid.NewGuid(),
                Address.Of("Colima 180", "Ciudad de México", "CDMX", "06700", "México"),
                Address.Of("Colima 180", "Ciudad de México", "CDMX", "06700", "México"),
                Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "3456")
            );
            order3.Add(MenuItemId.Of(new Guid("33333333-3333-3333-3333-333333333333")), 2, 175.00m);

            Order? order4 = Order.Create(
                OrderId.Of(new Guid("77777777-7777-7777-7777-777777777777")),
                CustomerId.Of(new Guid("11111111-1111-1111-1111-111111111111")),
                OrderNumber.Of("ORD-0004"),
                Guid.NewGuid(),
                Address.Of("Av. Vasconcelos 400", "San Pedro Garza García", "NL", "66220", "México"),
                Address.Of("Av. Vasconcelos 400", "San Pedro Garza García", "NL", "66220", "México"),
                Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Mastercard", "7654")
            );
            order4.Add(MenuItemId.Of(new Guid("22222222-2222-2222-2222-222222222222")), 2, 195.00m);
            order4.Add(MenuItemId.Of(new Guid("33333333-3333-3333-3333-333333333333")), 2, 175.00m);

            return [order1, order2, order3, order4];
        }
    }

    public static IEnumerable<OrderBill> OrderBills =>
        [
            OrderBill.Create(
                new Guid("44444444-4444-4444-4444-444444444444"),
                1,
                565.00m,
                90.40m,
                655.40m
            ),
            OrderBill.Create(
                new Guid("55555555-5555-5555-5555-555555555555"),
                2,
                195.00m,
                31.20m,
                226.20m
            ),
            OrderBill.Create(
                new Guid("66666666-6666-6666-6666-666666666666"),
                3,
                350.00m,
                56.00m,
                406.00m
            ),
            OrderBill.Create(
                new Guid("77777777-7777-7777-7777-777777777777"),
                4,
                740.00m,
                118.40m,
                858.40m
            )
        ];
}

namespace Ordering.Domain.Tests.Models;

/// <summary>
/// Covers <see cref="OrderBill.Create"/>. The factory is small but it sets defaults
/// for two enum fields that downstream billing/payment code relies on, plus it
/// snapshots the order totals at the moment the bill was created.
/// </summary>
public sealed class OrderBillTests
{
    /// <summary>
    /// Happy path: every numeric field round-trips through the factory so the
    /// caller can rely on <c>Amount</c>, <c>TaxAmount</c>, <c>TotalAmount</c>,
    /// and <c>BillNumber</c> being set exactly as supplied.
    /// </summary>
    [Fact]
    public void Create_SetsOrderIdBillNumberAndAmounts()
    {
        var orderId = Guid.NewGuid();

        var bill = OrderBill.Create(orderId, billNumber: 42, amount: 100m, taxAmount: 10m, totalAmount: 110m);

        bill.OrderId.Should().Be(orderId);
        bill.BillNumber.Should().Be(42);
        bill.Amount.Should().Be(100m);
        bill.TaxAmount.Should().Be(10m);
        bill.TotalAmount.Should().Be(110m);
    }

    /// <summary>
    /// Default-state contract: a freshly created bill must start in
    /// <see cref="PaymentStatus.Pending"/>. Anything else would imply the bill was
    /// already paid or voided before creation, which is nonsensical.
    /// </summary>
    [Fact]
    public void Create_DefaultsPaymentStatusToPending()
    {
        var bill = OrderBill.Create(Guid.NewGuid(), 1, 50m, 5m, 55m);

        bill.PaymentStatus.Should().Be(PaymentStatus.Pending);
    }

    /// <summary>
    /// Default-state contract: a freshly created bill defaults to
    /// <see cref="SplitType.Equal"/>. The bill can later be re-split explicitly.
    /// </summary>
    [Fact]
    public void Create_DefaultsSplitTypeToEqual()
    {
        var bill = OrderBill.Create(Guid.NewGuid(), 1, 50m, 5m, 55m);

        bill.SplitType.Should().Be(SplitType.Equal);
    }

    /// <summary>
    /// Documents that <c>Create</c> does not currently validate the supplied amounts:
    /// a zero-total bill is accepted without complaint. If/when a guard is added
    /// (e.g. <c>TotalAmount &gt;= 0</c>), this test should be updated to assert the
    /// exception instead of the successful creation.
    /// </summary>
    [Fact]
    public void Create_WithZeroTotal_IsAllowed()
    {
        var bill = OrderBill.Create(Guid.NewGuid(), 1, amount: 0m, taxAmount: 0m, totalAmount: 0m);

        bill.Amount.Should().Be(0m);
        bill.TaxAmount.Should().Be(0m);
        bill.TotalAmount.Should().Be(0m);
    }
}
using System.ComponentModel.DataAnnotations;

namespace Catalog.API.Tests.Unit;

/// <summary>
/// DataAnnotation validation tests for <see cref="CatalogOptions"/>. The
/// <c>ValidateOnStart()</c> registration in <c>Program.cs</c> is the gate —
/// these tests assert that out-of-range values are rejected by the same
/// attribute the host uses.
/// </summary>
public sealed class CatalogOptionsTests
{
    private static IList<ValidationResult> Validate(CatalogOptions options)
    {
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, ctx, results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    [InlineData(int.MaxValue)]
    public void CacheRepairIntervalMinutes_OutOfRange_FailsValidation(int minutes)
    {
        var errors = Validate(new CatalogOptions { CacheRepairIntervalMinutes = minutes });
        errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(1440)]
    public void CacheRepairIntervalMinutes_InRange_PassesValidation(int minutes)
    {
        var errors = Validate(new CatalogOptions { CacheRepairIntervalMinutes = minutes });
        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void MenuCacheTtlMinutes_OutOfRange_FailsValidation(int minutes)
    {
        var errors = Validate(new CatalogOptions { MenuCacheTtlMinutes = minutes });
        errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(1440)]
    public void MenuCacheTtlMinutes_InRange_PassesValidation(int minutes)
    {
        var errors = Validate(new CatalogOptions { MenuCacheTtlMinutes = minutes });
        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void IngredientCacheTtlMinutes_OutOfRange_FailsValidation(int minutes)
    {
        var errors = Validate(new CatalogOptions { IngredientCacheTtlMinutes = minutes });
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Defaults_AreInValidRange()
    {
        var errors = Validate(new CatalogOptions());
        errors.Should().BeEmpty();
    }
}
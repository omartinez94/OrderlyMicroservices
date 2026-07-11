using System.ComponentModel.DataAnnotations;

namespace Catalog.API.Tests.Unit;

/// <summary>
/// Range validation tests for
/// <see cref="MenuItemAnalyticsNightlyRecomputeServiceOptions"/>.
/// </summary>
public sealed class MenuItemAnalyticsNightlyOptionsTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void RunAtHour_OutOfRange_FailsValidation(int hour)
    {
        var opts = new MenuItemAnalyticsNightlyRecomputeServiceOptions { RunAtHour = hour };
        var ctx = new ValidationContext(opts);
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(opts, ctx, results, validateAllProperties: true);

        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(12)]
    [InlineData(23)]
    public void RunAtHour_InRange_PassesValidation(int hour)
    {
        var opts = new MenuItemAnalyticsNightlyRecomputeServiceOptions { RunAtHour = hour };
        var ctx = new ValidationContext(opts);
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(opts, ctx, results, validateAllProperties: true);

        results.Should().BeEmpty();
    }
}
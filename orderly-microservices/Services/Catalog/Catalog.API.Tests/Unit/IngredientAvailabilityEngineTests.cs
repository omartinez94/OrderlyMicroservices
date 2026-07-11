namespace Catalog.API.Tests.Unit;

/// <summary>
/// Pure-function rule-matrix tests for
/// <see cref="IngredientAvailabilityEngine.AvailabilityProfileFor"/>.
/// The engine has no I/O — tests feed in-memory rows directly.
/// </summary>
/// <remarks>
/// Covers the seven-case matrix from the spec plus the
/// <c>AllowAutoSubstitute</c> variants. Each test names the rule it
/// exercises so a regression points at the broken rule immediately.
/// </remarks>
public sealed class IngredientAvailabilityEngineTests
{
    // Convenience builders — keep the test bodies focused on the rule under test.

    private static IngredientAvailabilityEngine.MenuItemIngredientRow Required(int id) => new(id, IsOptional: false);
    private static IngredientAvailabilityEngine.MenuItemIngredientRow Optional(int id) => new(id, IsOptional: true);
    private static IngredientAvailabilityEngine.IngredientRow InStock(int id) => new(id, IsAvailable: true);
    private static IngredientAvailabilityEngine.IngredientRow OutOfStock(int id) => new(id, IsAvailable: false);
    private static IngredientAvailabilityEngine.AlternativeEdge Alt(int original, int alternative, bool autoSub = false) =>
        new(original, alternative, autoSub);

    // ───────────────────────── Rule 1: Available when everything required is in stock.

    [Fact]
    public void NoUnavailableIngredients_ReturnsAvailable()
    {
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1), Required(2)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = InStock(1),
                [2] = InStock(2),
            },
            alternatives: [],
            allowAutoSubstitute: false);

        result.Should().Be(IngredientAvailabilityProfile.Available);
    }

    [Fact]
    public void EmptyRequiredList_ReturnsAvailable()
    {
        // No required ingredients → vacuously satisfied → Available.
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>(),
            alternatives: [],
            allowAutoSubstitute: false);

        result.Should().Be(IngredientAvailabilityProfile.Available);
    }

    [Fact]
    public void OptionalIngredientOutOfStock_DoesNotAffectStatus()
    {
        // The optional garnish is out of stock; the required ingredient is in.
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1), Optional(2)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = InStock(1),
                [2] = OutOfStock(2),
            },
            alternatives: [],
            allowAutoSubstitute: false);

        result.Should().Be(IngredientAvailabilityProfile.Available);
    }

    // ───────────────────────── Rule 3: Unavailable when no alternative exists.

    [Fact]
    public void RequiredUnavailableNoAlternative_ReturnsUnavailable()
    {
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1), Required(2)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = OutOfStock(1),
                [2] = InStock(2),
            },
            alternatives: [],  // no alternatives for ingredient 1
            allowAutoSubstitute: false);

        result.Should().Be(IngredientAvailabilityProfile.Unavailable);
    }

    // ───────────────────────── Rule 4: Limited when an alternative is available.

    [Fact]
    public void RequiredUnavailableHasAvailableAlternative_ReturnsLimited()
    {
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = OutOfStock(1),
                [2] = InStock(2),
            },
            alternatives: [Alt(original: 1, alternative: 2, autoSub: false)],
            allowAutoSubstitute: false);

        // Rule 4: at least one alternative is available → Limited. The
        // chosen alternative id is recorded (informational; operator picks at
        // order time). allowAutoSubstitute=false keeps it Limited (Rule 5 only
        // kicks in when allowAutoSubstitute=true).
        result.Status.Should().Be(AvailabilityStatus.Limited);
        result.AutoSubstituteOf.Should().Be(2);
    }

    [Fact]
    public void RequiredUnavailableHasMultipleAlternatives_ReturnsLimited()
    {
        // 2 alternatives, both available — still Limited (operator picks).
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = OutOfStock(1),
                [2] = InStock(2),
                [3] = InStock(3),
            },
            alternatives:
            [
                Alt(original: 1, alternative: 2, autoSub: false),
                Alt(original: 1, alternative: 3, autoSub: false),
            ],
            allowAutoSubstitute: false);

        result.Status.Should().Be(AvailabilityStatus.Limited);
        // First-encountered available alternative wins for the informational id.
        result.AutoSubstituteOf.Should().Be(2);
    }

    [Fact]
    public void RequiredUnavailableHasAlternativeTargetOutOfStock_ReturnsUnavailable()
    {
        // The alternative exists but its target is also out of stock — no path.
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = OutOfStock(1),
                [2] = OutOfStock(2),
            },
            alternatives: [Alt(original: 1, alternative: 2, autoSub: true)],
            allowAutoSubstitute: true);

        result.Should().Be(IngredientAvailabilityProfile.Unavailable);
    }

    // ───────────────────────── Rule 5: AutoSubstitute flips Limited → Available.

    [Fact]
    public void AutoSubstituteSatisfied_ReturnsAvailableWithSubstituteId()
    {
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1), Required(2)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = OutOfStock(1),
                [2] = InStock(2),
                [3] = InStock(3),
                [4] = InStock(4),
            },
            alternatives:
            [
                Alt(original: 1, alternative: 2, autoSub: true),
                Alt(original: 3, alternative: 4, autoSub: true),
            ],
            allowAutoSubstitute: true);

        // Both unsatisfied requireds have exactly one autoSub alt → Available.
        result.Status.Should().Be(AvailabilityStatus.Available);
        result.AutoSubstituteOf.Should().NotBeNull();
    }

    [Fact]
    public void AutoSubstituteDisabledByRestaurantFlag_StaysLimited()
    {
        // Restaurant.AllowAutoSubstitute=false → engine returns Limited even
        // though the alternative is AutoSub=true. The chosen alt id is still
        // recorded (informational — operator picks at order time).
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = OutOfStock(1),
                [2] = InStock(2),
            },
            alternatives: [Alt(original: 1, alternative: 2, autoSub: true)],
            allowAutoSubstitute: false);

        result.Status.Should().Be(AvailabilityStatus.Limited);
        result.AutoSubstituteOf.Should().Be(2);
    }

    [Fact]
    public void AutoSubstituteMultipleAlternatives_StaysLimited()
    {
        // More than one autoSub candidate for the same original — the rule
        // defers to the operator (Limited).
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = OutOfStock(1),
                [2] = InStock(2),
                [3] = InStock(3),
            },
            alternatives:
            [
                Alt(original: 1, alternative: 2, autoSub: true),
                Alt(original: 1, alternative: 3, autoSub: true),
            ],
            allowAutoSubstitute: true);

        result.Status.Should().Be(AvailabilityStatus.Limited);
        // First-encountered available alt wins for the informational id.
        result.AutoSubstituteOf.Should().Be(2);
    }

    [Fact]
    public void AutoSubstituteMixedSubstitutability_ReturnsUnavailable()
    {
        // Ingredient 1 has one autoSub alt (in stock); ingredient 3 has
        // NO alternatives at all. Per Rule 3, an unsatisfied required
        // ingredient with no alternative path → Unavailable (not Limited).
        var result = IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: [Required(1), Required(3)],
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>
            {
                [1] = OutOfStock(1),
                [2] = InStock(2),
                [3] = OutOfStock(3),
            },
            alternatives: [Alt(original: 1, alternative: 2, autoSub: true)],
            allowAutoSubstitute: true);

        result.Status.Should().Be(AvailabilityStatus.Unavailable);
    }

    // ───────────────────────── Input validation.

    [Fact]
    public void NullRequiredIngredients_Throws()
    {
        var act = () => IngredientAvailabilityEngine.AvailabilityProfileFor(
            requiredIngredients: null!,
            ingredientAvailability: new Dictionary<int, IngredientAvailabilityEngine.IngredientRow>(),
            alternatives: [],
            allowAutoSubstitute: false);

        act.Should().Throw<ArgumentNullException>();
    }
}
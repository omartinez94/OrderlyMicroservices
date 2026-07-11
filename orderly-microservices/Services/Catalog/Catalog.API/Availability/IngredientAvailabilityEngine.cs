namespace Catalog.API.Availability;

/// <summary>
/// Pure, allocation-free-in-steady-state calculator for a single menu
/// item's derived availability. No I/O, no EF, no logging. The
/// orchestration code (domain-event handler + reconcile hosted service)
/// does the queries and passes the loaded rows in.
/// </summary>
/// <remarks>
/// <para><b>Why static + pure.</b> The engine runs once per affected menu
/// item per inbound domain event (and again every reconcile tick). It
/// must be allocation-free in steady state: the caller pre-loads the inputs into the argument lists
/// dictionaries, the engine walks them, returns a record. No LINQ chains
/// inside the engine itself.</para>
/// <list type="number">
///   <item>If every non-optional ingredient is available → <c>Available</c>.</item>
///   <item>If some non-optional ingredient is unavailable but has at
///         least one alternative whose target is available → <c>Limited</c>
///         (the chosen alternative's id becomes <c>AutoSubstituteOf</c>;
///         the operator picks at order time).</item>
///   <item>If any non-optional ingredient has no alternative and is
///         unavailable → <c>Unavailable</c>.</item>
///   <item>If <paramref name="allowAutoSubstitute"/> is true and every
///         unsatisfied non-optional ingredient has exactly one
///         <c>AutoSubstitute = true</c> alternative whose target is
///         available → <c>Available</c> with the chosen alternative's id
///         in <c>AutoSubstituteOf</c>.</item>
/// </list>
/// <para>Optional ingredients that are unavailable do <em>not</em> flip the
/// status (a missing optional garnish stays Available).</para>
/// </remarks>
public static class IngredientAvailabilityEngine
{
    /// <summary>One recipe row — an ingredient link with its optionality flag.</summary>
    /// <param name="IngredientId">FK to <c>Ingredient.Id</c>.</param>
    /// <param name="IsOptional">Whether the recipe row is required for the menu item's identity.</param>
    public sealed record MenuItemIngredientRow(int IngredientId, bool IsOptional);

    /// <summary>Availability snapshot for one ingredient.</summary>
    /// <param name="Id">FK to <c>Ingredient.Id</c>.</param>
    /// <param name="IsAvailable">Whether the ingredient is currently in stock (per <c>Ingredient.IsAvailable</c>).</param>
    public sealed record IngredientRow(int Id, bool IsAvailable);

    /// <summary>One alternative-edge row (original → alternative candidate).</summary>
    /// <param name="OriginalIngredientId">FK to the out-of-stock ingredient.</param>
    /// <param name="AlternativeIngredientId">FK to the replacement candidate.</param>
    /// <param name="AutoSubstitute">Whether the operator pre-authorizes this swap.</param>
    public sealed record AlternativeEdge(int OriginalIngredientId, int AlternativeIngredientId, bool AutoSubstitute);

    /// <summary>
    /// Compute the menu item's derived availability profile from the loaded
    /// rows. Pure function — same inputs always yield the same output.
    /// </summary>
    /// <param name="requiredIngredients">
    /// All recipe rows for the menu item (required + optional).
    /// </param>
    /// <param name="ingredientAvailability">
    /// Snapshot of <c>Ingredient.{Id, IsAvailable}</c> for every
    /// ingredient referenced in <paramref name="requiredIngredients"/>
    /// (and every alternative target — caller's responsibility).
    /// </param>
    /// <param name="alternatives">
    /// All <c>IngredientAlternative</c> rows whose
    /// <c>OriginalIngredientId</c> is in
    /// <paramref name="requiredIngredients"/>.
    /// </param>
    /// <param name="allowAutoSubstitute">
    /// Restaurant's <c>AllowAutoSubstitute</c> flag. When true and every
    /// unsatisfied required ingredient has exactly one
    /// <c>AutoSubstitute = true</c> alternative whose target is available,
    /// the engine resolves to <c>Available</c> instead of <c>Limited</c>.
    /// </param>
    /// <returns>
    /// The computed <see cref="IngredientAvailabilityProfile"/>.
    /// </returns>
    public static IngredientAvailabilityProfile AvailabilityProfileFor(
        IReadOnlyList<MenuItemIngredientRow> requiredIngredients,
        IReadOnlyDictionary<int, IngredientRow> ingredientAvailability,
        IReadOnlyList<AlternativeEdge> alternatives,
        bool allowAutoSubstitute)
    {
        ArgumentNullException.ThrowIfNull(requiredIngredients);
        ArgumentNullException.ThrowIfNull(ingredientAvailability);
        ArgumentNullException.ThrowIfNull(alternatives);

        // Bucket the alternatives by original-id once so the inner loop is O(1).
        // We pre-bucket via a single Dictionary on first miss; the dictionary
        // itself is the only allocation this method makes for non-empty inputs.
        Dictionary<int, List<AlternativeEdge>>? alternativesByOriginal = null;

        int? autoSubstituteWinner = null;

        foreach (var link in requiredIngredients)
        {
            // Optional unavailable ingredients are ignored entirely.
            if (link.IsOptional)
            {
                continue;
            }

            // Available? Move on.
            if (ingredientAvailability.TryGetValue(link.IngredientId, out var ingredient)
                && ingredient.IsAvailable)
            {
                continue;
            }

            // Unavailable required ingredient. Look up alternatives.
            alternativesByOriginal ??= BucketAlternatives(alternatives);
            if (!alternativesByOriginal.TryGetValue(link.IngredientId, out var candidates)
                || candidates is null
                || candidates.Count == 0)
            {
                // Rule 3: no alternative path.
                return IngredientAvailabilityProfile.Unavailable;
            }

            // Filter to candidates whose target ingredient is available.
            var availableCandidates = 0;
            int? firstAvailableCandidateId = null;
            int? autoSubCandidateId = null;
            var autoSubCandidateCount = 0;
            foreach (var candidate in candidates)
            {
                if (!ingredientAvailability.TryGetValue(candidate.AlternativeIngredientId, out var altIngredient)
                    || !altIngredient.IsAvailable)
                {
                    continue;
                }

                availableCandidates++;
                firstAvailableCandidateId ??= candidate.AlternativeIngredientId;
                if (candidate.AutoSubstitute)
                {
                    autoSubCandidateCount++;
                    autoSubCandidateId ??= candidate.AlternativeIngredientId;
                }
            }

            if (availableCandidates == 0)
            {
                // No candidate is currently available — treat as no path.
                return IngredientAvailabilityProfile.Unavailable;
            }

            // Rule 5: auto-substitute path. Three sub-cases:
            //   - allowAutoSubstitute=true + exactly 1 autoSub candidate:
            //     track the winner; let the outer loop continue to check the
            //     other unsatisfied ingredients.
            //   - allowAutoSubstitute=true + 2+ autoSub candidates for THIS
            //     original: too many choices — operator must pick → Rule 4 (Limited).
            //   - allowAutoSubstitute=false: skip; fall through to Rule 4 (Limited).
            if (allowAutoSubstitute && autoSubCandidateCount == 1)
            {
                autoSubstituteWinner ??= autoSubCandidateId;
                continue;
            }

            // Rule 4 (per ingredient): at least one alternative exists. The
            // whole item becomes Limited; the chosen id is informational.
            return IngredientAvailabilityProfile.Limited(firstAvailableCandidateId);
        }

        // All required ingredients satisfied (or no required ingredients at all).
        if (autoSubstituteWinner is not null)
        {
            return new IngredientAvailabilityProfile(AvailabilityStatus.Available, autoSubstituteWinner);
        }

        return IngredientAvailabilityProfile.Available;
    }

    private static Dictionary<int, List<AlternativeEdge>> BucketAlternatives(IReadOnlyList<AlternativeEdge> alternatives)
    {
        var bucket = new Dictionary<int, List<AlternativeEdge>>(capacity: alternatives.Count);
        foreach (var edge in alternatives)
        {
            if (!bucket.TryGetValue(edge.OriginalIngredientId, out var list))
            {
                list = [];
                bucket[edge.OriginalIngredientId] = list;
            }
            list.Add(edge);
        }
        return bucket;
    }
}
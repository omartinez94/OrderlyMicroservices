namespace Catalog.API.Availability;

/// <summary>
/// The engine's output: a menu item's derived availability status plus the
/// optional auto-substitute alternative id when the engine resolved a
/// single substitute. Used to drive both the
/// <c>MenuItem.AvailabilityStatus</c> write and the
/// <c>IngredientAvailabilityChangedIntegrationEvent</c> payload.
/// </summary>
/// <param name="Status">
/// One of <c>Available</c>, <c>Limited</c>, <c>Unavailable</c>. The engine
/// never returns a status outside this enum.
/// </param>
/// <param name="AutoSubstituteOf">
/// The alternative <c>Ingredient.Id</c> (int) when the engine resolved
/// exactly one auto-substitute. <see langword="null"/> otherwise (including
/// when the item is fully Available or when the substitute path is not
/// engaged). Matches <c>IngredientAlternative.AlternativeIngredientId</c>
/// which is int (the alternative id is an ingredient id, not a menu item id).
/// </param>
public sealed record IngredientAvailabilityProfile(AvailabilityStatus Status, int? AutoSubstituteOf)
{
    /// <summary>The fully-satisfied profile — every required ingredient available.</summary>
    public static readonly IngredientAvailabilityProfile Available = new(AvailabilityStatus.Available, null);

    /// <summary>No path to satisfy a required ingredient.</summary>
    public static readonly IngredientAvailabilityProfile Unavailable = new(AvailabilityStatus.Unavailable, null);

    /// <summary>Some unsatisfied ingredient has at least one alternative (manual choice left to the operator).</summary>
    /// <param name="autoSubstituteOf">A representative alternative id (informational only; operators choose at order time).</param>
    public static IngredientAvailabilityProfile Limited(int? autoSubstituteOf) =>
        new(AvailabilityStatus.Limited, autoSubstituteOf);
}
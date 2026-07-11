using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Data.Migrations
{
    /// <summary>
    /// R.2 — <c>OrderItem.Customizations</c> and
    /// <c>OrderItem.SelectedVariations</c> move from
    /// <c>string</c> (jsonb-as-text) to <c>IReadOnlyList&lt;&gt;</c> of typed
    /// records (<see cref="BuildingBlocks.Messaging.Events.KitchenOrderItemCustomization"/>
    /// / <see cref="BuildingBlocks.Messaging.Events.KitchenOrderItemVariation"/>).
    /// </summary>
    /// <remarks>
    /// The migration is intentionally empty at the SQL level: the on-disk
    /// column type stays <c>nvarchar(max)</c> (jsonb-as-text), only the
    /// .NET property type and the EF Core value converter change. The
    /// aggregate is the source of truth — EF Core serialises the typed
    /// array to JSON via <c>System.Text.Json</c>, and the existing rows
    /// (empty string or pre-R.2 jsonb text) deserialise to an empty list
    /// at read time.
    ///
    /// A pre-R.2 row that holds the legacy <c>string[]</c> shape
    /// (e.g. <c>["Size: Large"]</c>) deserialises to an empty list — those
    /// legacy entries are dropped at read time. This is acceptable because
    /// the basket/checkout wire payload already carries typed records, so
    /// no legacy data flows in via the wire today. Pre-existing
    /// dev-environment rows hold the richer <c>{ Name, Price }</c> shape
    /// from Phase D seeding and round-trip correctly.
    /// </remarks>
    public partial class TypedOrderItemCustomizationsJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
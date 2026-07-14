using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discount.Grpc.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxSupportToDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // outbox_messages table — same shape as BuildingBlocks.Messaging's
            // OutboxMessage entity. We hand-roll the migration here so the
            // SQLite engine matches the EF Core snapshot.
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            // ix_outbox_messages_dispatched_at_occurred_on is the dispatcher's
            // hot-path index (see OutboxMessageConfiguration.cs in
            // BuildingBlocks.Messaging). Lives here so the migration is the
            // single source of truth for the schema.
            migrationBuilder.Sql(
                "CREATE INDEX ix_outbox_messages_dispatched_at_occurred_on " +
                "ON outbox_messages (DispatchedAt, OccurredOn);");

            // outbox_messages_dead — quarantine for rows the dispatcher
            // couldn't route (today: SchemaVersion > MaxSupportedVersion;
            // future: poison payloads). Schema mirrors outbox_messages plus
            // Reason + RejectedAt.
            migrationBuilder.CreateTable(
                name: "outbox_messages_dead",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    RejectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages_dead", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_outbox_messages_dispatched_at_occurred_on;");
            migrationBuilder.DropTable(name: "outbox_messages_dead");
            migrationBuilder.DropTable(name: "outbox_messages");
        }
    }
}

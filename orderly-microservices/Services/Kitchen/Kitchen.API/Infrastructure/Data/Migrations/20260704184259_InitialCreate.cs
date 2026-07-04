using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Kitchen.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kitchen_stations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kitchen_stations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kitchen_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ReadyAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    BumpedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kitchen_tickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kitchen_ticket_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    SelectedVariations = table.Column<string[]>(type: "text[]", nullable: false),
                    Customizations = table.Column<string[]>(type: "text[]", nullable: false),
                    SpecialInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SeatNumber = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ReadyAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    StationId = table.Column<Guid>(type: "uuid", nullable: true),
                    KitchenTicketId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kitchen_ticket_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kitchen_ticket_items_kitchen_tickets_KitchenTicketId",
                        column: x => x.KitchenTicketId,
                        principalTable: "kitchen_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_stations_RestaurantId_IsActive",
                table: "kitchen_stations",
                columns: new[] { "RestaurantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_ticket_items_KitchenTicketId",
                table: "kitchen_ticket_items",
                column: "KitchenTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_tickets_ReceivedAt",
                table: "kitchen_tickets",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_tickets_RestaurantId",
                table: "kitchen_tickets",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_tickets_Status",
                table: "kitchen_tickets",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kitchen_stations");

            migrationBuilder.DropTable(
                name: "kitchen_ticket_items");

            migrationBuilder.DropTable(
                name: "kitchen_tickets");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Kitchen.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCancelledPendingAckAndCancelRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelRequestReason",
                table: "kitchen_tickets",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "CancelRequestedAt",
                table: "kitchen_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "CompletedAt",
                table: "kitchen_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelRequested",
                table: "kitchen_tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Instant>(
                name: "StatusChangedAt",
                table: "kitchen_tickets",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelRequestReason",
                table: "kitchen_tickets");

            migrationBuilder.DropColumn(
                name: "CancelRequestedAt",
                table: "kitchen_tickets");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "kitchen_tickets");

            migrationBuilder.DropColumn(
                name: "IsCancelRequested",
                table: "kitchen_tickets");

            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                table: "kitchen_tickets");
        }
    }
}

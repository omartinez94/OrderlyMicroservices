using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discount.Grpc.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountRulesAndProcessedInboundEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedInboundevents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsumerType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedInboundevents", x => new { x.EventId, x.ConsumerType });
                });

            migrationBuilder.CreateIndex(
                name: "ix_processed_inbound_consumer_time",
                table: "ProcessedInboundevents",
                columns: new[] { "ConsumerType", "ConsumedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedInboundevents");
        }
    }
}

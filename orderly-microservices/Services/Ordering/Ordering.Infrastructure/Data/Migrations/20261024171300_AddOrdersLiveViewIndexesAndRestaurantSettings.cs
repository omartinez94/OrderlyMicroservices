using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersLiveViewIndexesAndRestaurantSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "Pending",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "Pending");

            migrationBuilder.CreateTable(
                name: "RestaurantSettings",
                columns: table => new
                {
                    RestaurantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantSettings", x => x.RestaurantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RestaurantId_LastModified",
                table: "Orders",
                columns: new[] { "RestaurantId", "LastModified" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RestaurantId_Status_CreatedAt",
                table: "Orders",
                columns: new[] { "RestaurantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestaurantSettings");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RestaurantId_LastModified",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RestaurantId_Status_CreatedAt",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Pending",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldDefaultValue: "Pending");
        }
    }
}

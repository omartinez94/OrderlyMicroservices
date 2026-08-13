using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Catalog.API.Migrations
{
    /// <inheritdoc />
    public partial class SeedAcmeBrandAndRestaurant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Brands",
                columns: new[] { "Id", "ContactEmail", "ContactPhone", "CreatedAt", "CreatedBy", "CuisineType", "Description", "LastModifiedAt", "LastModifiedBy", "LogoUrl", "Name", "WebsiteUrl" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), "", "", NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", 16, "", NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "https://placehold.co/100x100/png?text=Acme", "Acme", "" });

            migrationBuilder.InsertData(
                table: "Restaurants",
                columns: new[] { "Id", "Address", "AllowAutoSubstitute", "AutoConfirmOrders", "AutoConfirmReservations", "BrandId", "CreatedAt", "CreatedBy", "Currency", "Email", "EstimatedTurnoverMinutes", "LastModifiedAt", "LastModifiedBy", "Name", "PhoneNumber", "TaxRate", "TimeZone" },
                values: new object[] { new Guid("22222222-2222-2222-2222-222222222222"), "123 Acme St", false, false, false, new Guid("11111111-1111-1111-1111-111111111111"), NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "MXN", "", 30, NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "Acme", "", 0.16m, "America/Monterrey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "Restaurants",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));
        }
    }
}

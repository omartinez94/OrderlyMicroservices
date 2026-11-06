using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Catalog.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSeededBrandsAndRestaurants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "Restaurants",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));

            migrationBuilder.InsertData(
                table: "Brands",
                columns: new[] { "Id", "ContactEmail", "ContactPhone", "CreatedAt", "CreatedBy", "CuisineType", "Description", "LastModifiedAt", "LastModifiedBy", "LogoUrl", "Name", "ShortId", "WebsiteUrl" },
                values: new object[,]
                {
                    { new Guid("a4f1c8e2-6b3d-4e9a-9f5c-8d1e2a3b4c5d"), "contacto@acme.mx", "+52 55 5584 9231", NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", 2, "Cantina tradicional y cocina mexicana contemporánea.", NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "https://placehold.co/100x100/png?text=Acme", "Acme", "brdm7k8x9pq", "https://acme.mx" },
                    { new Guid("f2c9e1b4-7a5d-4c8e-8b3f-1d4e5a6b7c8d"), "contacto@kalaa.mx", "+52 55 5280 4421", NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", 2, "Alta gastronomía mexicana de raíz con técnicas ancestrales.", NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "https://placehold.co/100x100/png?text=Kalaa", "Kalaa", "brdp3w4e6tr", "https://kalaa.mx" }
                });

            migrationBuilder.InsertData(
                table: "Restaurants",
                columns: new[] { "Id", "Address", "AllowAutoSubstitute", "AutoConfirmOrders", "AutoConfirmReservations", "BrandId", "CreatedAt", "CreatedBy", "Currency", "Email", "EstimatedTurnoverMinutes", "LastModifiedAt", "LastModifiedBy", "Name", "PhoneNumber", "ShortId", "TaxRate", "TimeZone" },
                values: new object[,]
                {
                    { new Guid("c5b1e9a4-3f7d-4a8e-9c2b-6d7e8f1a2b3c"), "Av. Vasconcelos 345, Del Valle, 66220 San Pedro Garza García, N.L.", false, false, false, new Guid("a4f1c8e2-6b3d-4e9a-9f5c-8d1e2a3b4c5d"), NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "MXN", "sanpedro@acme.mx", 30, NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "Acme Grill & Bar - San Pedro", "+52 81 8356 7410", "rstc9h4w7tz", 0.16m, "America/Monterrey" },
                    { new Guid("d1a7f3e5-8c2b-4e9d-9a4f-5b6c7d8e9f0a"), "Campos Elíseos 204, Polanco, Miguel Hidalgo, 11560 Ciudad de México, CDMX", false, false, false, new Guid("f2c9e1b4-7a5d-4c8e-8b3f-1d4e5a6b7c8d"), NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "MXN", "polanco@kalaa.mx", 30, NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "Kalaa Cocina Raíz - Polanco", "+52 55 5280 4421", "rstk2m8b4qy", 0.16m, "America/Mexico_City" },
                    { new Guid("e8d2a1b5-9c4f-4e7a-8f1d-2b3c4d5e6a7b"), "Colima 168, Roma Norte, Cuauhtémoc, 06700 Ciudad de México, CDMX", false, false, false, new Guid("a4f1c8e2-6b3d-4e9a-9f5c-8d1e2a3b4c5d"), NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "MXN", "romanorte@acme.mx", 30, NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "Acme Cantina Tradicional - Roma Norte", "+52 55 5584 9231", "rstv8n2k5mx", 0.16m, "America/Mexico_City" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("a4f1c8e2-6b3d-4e9a-9f5c-8d1e2a3b4c5d"));

            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("f2c9e1b4-7a5d-4c8e-8b3f-1d4e5a6b7c8d"));

            migrationBuilder.DeleteData(
                table: "Restaurants",
                keyColumn: "Id",
                keyValue: new Guid("c5b1e9a4-3f7d-4a8e-9c2b-6d7e8f1a2b3c"));

            migrationBuilder.DeleteData(
                table: "Restaurants",
                keyColumn: "Id",
                keyValue: new Guid("d1a7f3e5-8c2b-4e9d-9a4f-5b6c7d8e9f0a"));

            migrationBuilder.DeleteData(
                table: "Restaurants",
                keyColumn: "Id",
                keyValue: new Guid("e8d2a1b5-9c4f-4e7a-8f1d-2b3c4d5e6a7b"));

            migrationBuilder.InsertData(
                table: "Brands",
                columns: new[] { "Id", "ContactEmail", "ContactPhone", "CreatedAt", "CreatedBy", "CuisineType", "Description", "LastModifiedAt", "LastModifiedBy", "LogoUrl", "Name", "ShortId", "WebsiteUrl" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), "", "", NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", 16, "", NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "https://placehold.co/100x100/png?text=Acme", "Acme", "brd11111111", "" });

            migrationBuilder.InsertData(
                table: "Restaurants",
                columns: new[] { "Id", "Address", "AllowAutoSubstitute", "AutoConfirmOrders", "AutoConfirmReservations", "BrandId", "CreatedAt", "CreatedBy", "Currency", "Email", "EstimatedTurnoverMinutes", "LastModifiedAt", "LastModifiedBy", "Name", "PhoneNumber", "ShortId", "TaxRate", "TimeZone" },
                values: new object[] { new Guid("22222222-2222-2222-2222-222222222222"), "123 Acme St", false, false, false, new Guid("11111111-1111-1111-1111-111111111111"), NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "MXN", "", 30, NodaTime.Instant.FromUnixTimeTicks(17672256000000000L), "system", "Acme", "", "rst22222222", 0.16m, "America/Monterrey" });
        }
    }
}

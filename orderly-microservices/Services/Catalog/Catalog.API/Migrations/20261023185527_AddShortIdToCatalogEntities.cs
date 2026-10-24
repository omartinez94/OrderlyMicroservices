using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.API.Migrations
{
    /// <inheritdoc />
    public partial class AddShortIdToCatalogEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortId",
                table: "Tables",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShortId",
                table: "Restaurants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShortId",
                table: "MenuItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShortId",
                table: "Brands",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "ShortId",
                value: "brd11111111");

            migrationBuilder.UpdateData(
                table: "Restaurants",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "ShortId",
                value: "rst22222222");

            migrationBuilder.Sql(@"
                UPDATE ""Tables"" SET ""ShortId"" = CONCAT('tbl', LPAD(sub.seq::text, 8, '0')) FROM (SELECT ""Id"", ROW_NUMBER() OVER (ORDER BY ""Id"") as seq FROM ""Tables"") sub WHERE ""Tables"".""Id"" = sub.""Id"";
                UPDATE ""MenuItems"" SET ""ShortId"" = CONCAT('itm', LPAD(sub.seq::text, 8, '0')) FROM (SELECT ""Id"", ROW_NUMBER() OVER (ORDER BY ""Id"") as seq FROM ""MenuItems"") sub WHERE ""MenuItems"".""Id"" = sub.""Id"";
                UPDATE ""Restaurants"" SET ""ShortId"" = CONCAT('rst', LPAD(sub.seq::text, 8, '0')) FROM (SELECT ""Id"", ROW_NUMBER() OVER (ORDER BY ""Id"") as seq FROM ""Restaurants"") sub WHERE ""Restaurants"".""Id"" = sub.""Id"" AND ""Restaurants"".""ShortId"" = '';
                UPDATE ""Brands"" SET ""ShortId"" = CONCAT('brd', LPAD(sub.seq::text, 8, '0')) FROM (SELECT ""Id"", ROW_NUMBER() OVER (ORDER BY ""Id"") as seq FROM ""Brands"") sub WHERE ""Brands"".""Id"" = sub.""Id"" AND ""Brands"".""ShortId"" = '';
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Tables_ShortId",
                table: "Tables",
                column: "ShortId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Restaurants_ShortId",
                table: "Restaurants",
                column: "ShortId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_ShortId",
                table: "MenuItems",
                column: "ShortId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brands_ShortId",
                table: "Brands",
                column: "ShortId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tables_ShortId",
                table: "Tables");

            migrationBuilder.DropIndex(
                name: "IX_Restaurants_ShortId",
                table: "Restaurants");

            migrationBuilder.DropIndex(
                name: "IX_MenuItems_ShortId",
                table: "MenuItems");

            migrationBuilder.DropIndex(
                name: "IX_Brands_ShortId",
                table: "Brands");

            migrationBuilder.DropColumn(
                name: "ShortId",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "ShortId",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "ShortId",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "ShortId",
                table: "Brands");
        }
    }
}

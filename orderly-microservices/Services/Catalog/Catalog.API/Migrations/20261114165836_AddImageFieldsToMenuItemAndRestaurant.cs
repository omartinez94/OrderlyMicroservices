using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.API.Migrations
{
    /// <inheritdoc />
    public partial class AddImageFieldsToMenuItemAndRestaurant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BannerUrl",
                table: "Restaurants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Restaurants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageUploadStatus",
                table: "MenuItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PendingImageKey",
                table: "MenuItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "MenuItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Restaurants",
                keyColumn: "Id",
                keyValue: new Guid("c5b1e9a4-3f7d-4a8e-9c2b-6d7e8f1a2b3c"),
                columns: new[] { "BannerUrl", "LogoUrl" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Restaurants",
                keyColumn: "Id",
                keyValue: new Guid("d1a7f3e5-8c2b-4e9d-9a4f-5b6c7d8e9f0a"),
                columns: new[] { "BannerUrl", "LogoUrl" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Restaurants",
                keyColumn: "Id",
                keyValue: new Guid("e8d2a1b5-9c4f-4e7a-8f1d-2b3c4d5e6a7b"),
                columns: new[] { "BannerUrl", "LogoUrl" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BannerUrl",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "ImageUploadStatus",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "PendingImageKey",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "MenuItems");
        }
    }
}

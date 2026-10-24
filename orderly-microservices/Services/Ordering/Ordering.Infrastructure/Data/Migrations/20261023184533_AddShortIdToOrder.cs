using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShortIdToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortId",
                table: "Orders",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE Orders
                SET ShortId = CONCAT('ord', LOWER(SUBSTRING(REPLACE(CONVERT(varchar(36), Id), '-', ''), 1, 8)))
                WHERE ShortId = '' OR ShortId IS NULL;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShortId",
                table: "Orders",
                column: "ShortId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_ShortId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShortId",
                table: "Orders");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RedactPaymentToPaymentMethodSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Payment_CardName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Payment_CardNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Payment_Ccv",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Payment_Expiration",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "Payment_PaymentMethod",
                table: "Orders",
                newName: "Payment_Brand");

            migrationBuilder.AddColumn<string>(
                name: "Payment_LastFour",
                table: "Orders",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Payment_Method",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Payment_LastFour",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Payment_Method",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "Payment_Brand",
                table: "Orders",
                newName: "Payment_PaymentMethod");

            migrationBuilder.AddColumn<string>(
                name: "Payment_CardName",
                table: "Orders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Payment_CardNumber",
                table: "Orders",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Payment_Ccv",
                table: "Orders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Payment_Expiration",
                table: "Orders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");
        }
    }
}

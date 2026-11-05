using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kitchen.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseNumberToKitchenTicketItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CourseNumber",
                table: "kitchen_ticket_items",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CourseNumber",
                table: "kitchen_ticket_items");
        }
    }
}

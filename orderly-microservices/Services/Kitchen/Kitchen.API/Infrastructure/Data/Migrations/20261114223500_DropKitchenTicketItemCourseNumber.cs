using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kitchen.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(KitchenDbContext))]
    [Migration("20261114223500_DropKitchenTicketItemCourseNumber")]
    public partial class DropKitchenTicketItemCourseNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CourseNumber",
                table: "kitchen_ticket_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CourseNumber",
                table: "kitchen_ticket_items",
                type: "integer",
                nullable: true);
        }
    }
}

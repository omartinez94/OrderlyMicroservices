using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discount.Grpc.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteToCoupon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Soft-delete columns — null = alive. The global query filter
            // (DiscountContext.OnModelCreating) excludes rows where
            // DeletedAt IS NOT NULL from every Coupon read query.
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Coupons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Coupons",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DeletedBy", table: "Coupons");
            migrationBuilder.DropColumn(name: "DeletedAt", table: "Coupons");
        }
    }
}

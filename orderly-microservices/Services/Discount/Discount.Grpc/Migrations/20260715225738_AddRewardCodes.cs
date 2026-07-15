using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discount.Grpc.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RewardCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RestaurantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<decimal>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ExpirationDate = table.Column<long>(type: "INTEGER", nullable: true),
                    RedeemAmount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRedeemAmount = table.Column<int>(type: "INTEGER", nullable: true),
                    RedeemedInOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RedeemedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DeletedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reward_codes_restaurant_active_expiry",
                table: "RewardCodes",
                columns: new[] { "RestaurantId", "IsActive", "ExpirationDate" });

            migrationBuilder.CreateIndex(
                name: "ux_reward_codes_restaurant_code",
                table: "RewardCodes",
                columns: new[] { "RestaurantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RewardCodes");
        }
    }
}

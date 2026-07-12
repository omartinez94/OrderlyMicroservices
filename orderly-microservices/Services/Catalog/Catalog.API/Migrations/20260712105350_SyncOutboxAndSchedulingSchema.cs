using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Catalog.API.Migrations
{
    /// <inheritdoc />
    public partial class SyncOutboxAndSchedulingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContactPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CuisineType = table.Column<int>(type: "integer", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WebsiteUrl = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BulkOrderUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ErrorLog = table.Column<string>(type: "text", nullable: false),
                    FailedRows = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SuccessfulRows = table.Column<int>(type: "integer", nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ApprovedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkOrderUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerFeedbacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Comments = table.Column<string>(type: "text", nullable: false),
                    FoodQualityRating = table.Column<int>(type: "integer", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverallRating = table.Column<int>(type: "integer", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RewardCode = table.Column<string>(type: "text", nullable: false),
                    RewardDescription = table.Column<string>(type: "text", nullable: false),
                    RewardRedeemed = table.Column<bool>(type: "boolean", nullable: false),
                    RewardType = table.Column<string>(type: "text", nullable: false),
                    ServiceSpeedRating = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    WaiterFriendlinessRating = table.Column<int>(type: "integer", nullable: false),
                    RedeemedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    RedeemedInOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RewardValue = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderTimingAnalytics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActualPrepMinutes = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedToPreparingSeconds = table.Column<int>(type: "integer", nullable: false),
                    EstimatedPrepMinutes = table.Column<int>(type: "integer", nullable: false),
                    KitchenStaffCount = table.Column<int>(type: "integer", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderingToPendingSeconds = table.Column<int>(type: "integer", nullable: false),
                    PendingToConfirmedSeconds = table.Column<int>(type: "integer", nullable: false),
                    PreparingToReadySeconds = table.Column<int>(type: "integer", nullable: false),
                    PrepTimeDifferenceMinutes = table.Column<int>(type: "integer", nullable: false),
                    ReadyToDeliveredSeconds = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    WaiterUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderTimingAnalytics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages_dead",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages_dead", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "processed_order_items",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_order_items", x => new { x.OrderId, x.MenuItemId });
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerEmail = table.Column<string>(type: "text", nullable: false),
                    CustomerName = table.Column<string>(type: "text", nullable: false),
                    CustomerPhone = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    PartySize = table.Column<int>(type: "integer", nullable: false),
                    ReminderSent = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    ReservationDate = table.Column<LocalDate>(type: "date", nullable: false),
                    ReservationNumber = table.Column<string>(type: "text", nullable: false),
                    ReservationTime = table.Column<LocalTime>(type: "time", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpecialRequests = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReminderSentAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    SeatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    TableId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Restaurants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    AllowAutoSubstitute = table.Column<bool>(type: "boolean", nullable: false),
                    AutoConfirmOrders = table.Column<bool>(type: "boolean", nullable: false),
                    AutoConfirmReservations = table.Column<bool>(type: "boolean", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "MXN"),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EstimatedTurnoverMinutes = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "America/Monterrey"),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Restaurants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalkInQueues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerName = table.Column<string>(type: "text", nullable: false),
                    CustomerPhone = table.Column<string>(type: "text", nullable: false),
                    EstimatedWaitMinutes = table.Column<int>(type: "integer", nullable: false),
                    PartySize = table.Column<int>(type: "integer", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NotifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    SeatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    TableId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalkInQueues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ingredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CurrentStock = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MinimumStock = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ingredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ingredients_Restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuCategories_Restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    PositionX = table.Column<int>(type: "integer", nullable: false),
                    PositionY = table.Column<int>(type: "integer", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Shape = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "rectangle"),
                    Status = table.Column<int>(type: "integer", maxLength: 20, nullable: false, defaultValue: 0),
                    TableNumber = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CurrentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tables_Restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IngredientAlternatives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AlternativeIngredientId = table.Column<int>(type: "integer", nullable: false),
                    AutoSubstitute = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    OriginalIngredientId = table.Column<int>(type: "integer", nullable: false),
                    PriceModifier = table.Column<decimal>(type: "numeric(10,2)", nullable: false, defaultValue: 0m),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientAlternatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientAlternatives_Ingredients_AlternativeIngredientId",
                        column: x => x.AlternativeIngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IngredientAlternatives_Ingredients_OriginalIngredientId",
                        column: x => x.OriginalIngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IngredientAlternatives_Restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuSubCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeletedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuSubCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuSubCategories_MenuCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "MenuCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MergedTables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildTableId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MergedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ParentTableId = table.Column<Guid>(type: "uuid", nullable: false),
                    SplitAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MergedTables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MergedTables_Tables_ChildTableId",
                        column: x => x.ChildTableId,
                        principalTable: "Tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MergedTables_Tables_ParentTableId",
                        column: x => x.ParentTableId,
                        principalTable: "Tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailabilityStatus = table.Column<int>(type: "integer", maxLength: 20, nullable: false, defaultValue: 0),
                    BasePrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ItemType = table.Column<int>(type: "integer", maxLength: 20, nullable: false, defaultValue: 0),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PrepTimeMaxMinutes = table.Column<int>(type: "integer", nullable: false),
                    PrepTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    PromoEndDate = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    PromoPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    PromoStartDate = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    SeasonEndDate = table.Column<LocalDate>(type: "date", nullable: true),
                    SeasonStartDate = table.Column<LocalDate>(type: "date", nullable: true),
                    SubCategoryId = table.Column<int>(type: "integer", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItems_MenuSubCategories_SubCategoryId",
                        column: x => x.SubCategoryId,
                        principalTable: "MenuSubCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MenuItems_Restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComboItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ComboMenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncludedMenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComboItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComboItems_MenuItems_ComboMenuItemId",
                        column: x => x.ComboMenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComboItems_MenuItems_IncludedMenuItemId",
                        column: x => x.IncludedMenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuItemAnalytics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AfternoonOrders = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    AnalysisDate = table.Column<LocalDate>(type: "date", nullable: false),
                    AvgPrepTimeMinutes = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    EveningOrders = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MorningOrders = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NightOrders = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimesModified = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TimesOrdered = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TimesOutOfStock = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalRevenue = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemAnalytics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItemAnalytics_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuItemAnalytics_Restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuItemIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IngredientId = table.Column<int>(type: "integer", nullable: false),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityRequired = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItemIngredients_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MenuItemIngredients_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MenuItemVariations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PriceModifier = table.Column<decimal>(type: "numeric(10,2)", nullable: false, defaultValue: 0m),
                    VariationValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeletedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemVariations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItemVariations_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    EffectiveDate = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    NewPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    OldPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    PriceType = table.Column<int>(type: "integer", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientAlternativeId = table.Column<int>(type: "integer", nullable: true),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    VariationId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceHistory_IngredientAlternatives_IngredientAlternativeId",
                        column: x => x.IngredientAlternativeId,
                        principalTable: "IngredientAlternatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PriceHistory_MenuItemVariations_VariationId",
                        column: x => x.VariationId,
                        principalTable: "MenuItemVariations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PriceHistory_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PriceHistory_Restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Brands_IsActive",
                table: "Brands",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Name",
                table: "Brands",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ComboItems_ComboMenuItemId",
                table: "ComboItems",
                column: "ComboMenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ComboItems_IncludedMenuItemId",
                table: "ComboItems",
                column: "IncludedMenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientAlternatives_AlternativeIngredientId",
                table: "IngredientAlternatives",
                column: "AlternativeIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientAlternatives_OriginalIngredientId",
                table: "IngredientAlternatives",
                column: "OriginalIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientAlternatives_OriginalIngredientId_AlternativeIngr~",
                table: "IngredientAlternatives",
                columns: new[] { "OriginalIngredientId", "AlternativeIngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientAlternatives_RestaurantId",
                table: "IngredientAlternatives",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_IsAvailable",
                table: "Ingredients",
                column: "IsAvailable");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_RestaurantId",
                table: "Ingredients",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_RestaurantId_Name",
                table: "Ingredients",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuCategories_IsActive",
                table: "MenuCategories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_MenuCategories_IsDeleted",
                table: "MenuCategories",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MenuCategories_RestaurantId",
                table: "MenuCategories",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemAnalytics_AnalysisDate",
                table: "MenuItemAnalytics",
                column: "AnalysisDate");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemAnalytics_MenuItemId",
                table: "MenuItemAnalytics",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemAnalytics_MenuItemId_AnalysisDate",
                table: "MenuItemAnalytics",
                columns: new[] { "MenuItemId", "AnalysisDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemAnalytics_RestaurantId",
                table: "MenuItemAnalytics",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemIngredients_IngredientId",
                table: "MenuItemIngredients",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemIngredients_MenuItemId",
                table: "MenuItemIngredients",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemIngredients_MenuItemId_IngredientId",
                table: "MenuItemIngredients",
                columns: new[] { "MenuItemId", "IngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_IsActive",
                table: "MenuItems",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_IsAvailable",
                table: "MenuItems",
                column: "IsAvailable");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_IsDeleted",
                table: "MenuItems",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_RestaurantId",
                table: "MenuItems",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_RestaurantId_IsAvailable_IsActive",
                table: "MenuItems",
                columns: new[] { "RestaurantId", "IsAvailable", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_SubCategoryId",
                table: "MenuItems",
                column: "SubCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemVariations_IsDeleted",
                table: "MenuItemVariations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemVariations_MenuItemId",
                table: "MenuItemVariations",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuSubCategories_CategoryId",
                table: "MenuSubCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuSubCategories_IsActive",
                table: "MenuSubCategories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_MenuSubCategories_IsDeleted",
                table: "MenuSubCategories",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MergedTables_ChildTableId",
                table: "MergedTables",
                column: "ChildTableId");

            migrationBuilder.CreateIndex(
                name: "IX_MergedTables_ParentTableId_ChildTableId",
                table: "MergedTables",
                columns: new[] { "ParentTableId", "ChildTableId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dispatched_at_occurred_on",
                table: "outbox_messages",
                columns: new[] { "DispatchedAt", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_rejected_at",
                table: "outbox_messages_dead",
                column: "RejectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistory_CreatedAt",
                table: "PriceHistory",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistory_EffectiveDate",
                table: "PriceHistory",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistory_IngredientAlternativeId",
                table: "PriceHistory",
                column: "IngredientAlternativeId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistory_MenuItemId",
                table: "PriceHistory",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistory_RestaurantId",
                table: "PriceHistory",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistory_VariationId",
                table: "PriceHistory",
                column: "VariationId");

            migrationBuilder.CreateIndex(
                name: "IX_Restaurants_BrandId",
                table: "Restaurants",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Restaurants_IsActive",
                table: "Restaurants",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Tables_RestaurantId",
                table: "Tables",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tables_RestaurantId_TableNumber",
                table: "Tables",
                columns: new[] { "RestaurantId", "TableNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tables_Status",
                table: "Tables",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropTable(
                name: "BulkOrderUploads");

            migrationBuilder.DropTable(
                name: "ComboItems");

            migrationBuilder.DropTable(
                name: "CustomerFeedbacks");

            migrationBuilder.DropTable(
                name: "MenuItemAnalytics");

            migrationBuilder.DropTable(
                name: "MenuItemIngredients");

            migrationBuilder.DropTable(
                name: "MergedTables");

            migrationBuilder.DropTable(
                name: "OrderTimingAnalytics");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages_dead");

            migrationBuilder.DropTable(
                name: "PriceHistory");

            migrationBuilder.DropTable(
                name: "processed_order_items");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WalkInQueues");

            migrationBuilder.DropTable(
                name: "Tables");

            migrationBuilder.DropTable(
                name: "IngredientAlternatives");

            migrationBuilder.DropTable(
                name: "MenuItemVariations");

            migrationBuilder.DropTable(
                name: "Ingredients");

            migrationBuilder.DropTable(
                name: "MenuItems");

            migrationBuilder.DropTable(
                name: "MenuSubCategories");

            migrationBuilder.DropTable(
                name: "MenuCategories");

            migrationBuilder.DropTable(
                name: "Restaurants");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StaySphere.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "pricing");

            migrationBuilder.EnsureSchema(
                name: "engagement");

            migrationBuilder.EnsureSchema(
                name: "trust");

            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "booking");

            migrationBuilder.EnsureSchema(
                name: "reviews");

            migrationBuilder.CreateTable(
                name: "AiPendingActions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPendingActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Amenities",
                schema: "catalog",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Amenities", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                schema: "engagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GuestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GuestLastReadMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HostLastReadMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsReported = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Coupons",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PercentOff = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    AmountOff = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    MinimumSpend = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: false),
                    Redemptions = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.CheckConstraint("CK_Coupons_Percent", "[PercentOff] BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "FraudChecks",
                schema: "trust",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RuleCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Acknowledged = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FraudChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "platform",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseStatus = table.Column<int>(type: "int", nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", maxLength: -1, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.Scope, x.UserId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "platform",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Account = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "engagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", maxLength: -1, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                schema: "trust",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReporterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                schema: "trust",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Bio = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AvatarUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreferredCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEvents",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "engagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "engagement",
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketMessages",
                schema: "trust",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromAgent = table.Column<bool>(type: "bit", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketMessages_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "trust",
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Properties",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    PropertyType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RoomType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MaxGuests = table.Column<int>(type: "int", nullable: false),
                    Bedrooms = table.Column<int>(type: "int", nullable: false),
                    Beds = table.Column<int>(type: "int", nullable: false),
                    Bathrooms = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    BasePrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CleaningFee = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    WeekendAdjustmentPercent = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    WeeklyDiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MonthlyDiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InstantBook = table.Column<bool>(type: "bit", nullable: false),
                    PetsAllowed = table.Column<bool>(type: "bit", nullable: false),
                    SmokingAllowed = table.Column<bool>(type: "bit", nullable: false),
                    EventsAllowed = table.Column<bool>(type: "bit", nullable: false),
                    HouseRules = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CheckInTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    CheckOutTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    CancellationPolicy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MinNights = table.Column<int>(type: "int", nullable: false),
                    RatingAverage = table.Column<double>(type: "float", nullable: false),
                    ReviewCount = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Properties", x => x.Id);
                    table.CheckConstraint("CK_Properties_BasePrice", "[BasePrice] >= 0");
                    table.CheckConstraint("CK_Properties_CleaningFee", "[CleaningFee] >= 0");
                    table.CheckConstraint("CK_Properties_MaxGuests", "[MaxGuests] BETWEEN 1 AND 50");
                    table.ForeignKey(
                        name: "FK_Properties_Users_HostId",
                        column: x => x.HostId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.Role });
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockedDates",
                schema: "pricing",
                columns: table => new
                {
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedDates", x => new { x.PropertyId, x.Date });
                    table.ForeignKey(
                        name: "FK_BlockedDates_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalSchema: "catalog",
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                schema: "engagement",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => new { x.UserId, x.PropertyId });
                    table.ForeignKey(
                        name: "FK_Favorites_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalSchema: "catalog",
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyAmenities",
                schema: "catalog",
                columns: table => new
                {
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmenityCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyAmenities", x => new { x.PropertyId, x.AmenityCode });
                    table.ForeignKey(
                        name: "FK_PropertyAmenities_Amenities_AmenityCode",
                        column: x => x.AmenityCode,
                        principalSchema: "catalog",
                        principalTable: "Amenities",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PropertyAmenities_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalSchema: "catalog",
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyImages",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    BlobKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyImages_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalSchema: "catalog",
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                schema: "booking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckIn = table.Column<DateOnly>(type: "date", nullable: false),
                    CheckOut = table.Column<DateOnly>(type: "date", nullable: false),
                    Guests = table.Column<int>(type: "int", nullable: false),
                    Nights = table.Column<int>(type: "int", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CleaningFee = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ServiceFee = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Taxes = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    CouponCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CancellationPolicy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HoldExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_Dates", "[CheckOut] > [CheckIn]");
                    table.CheckConstraint("CK_Reservations_Guests", "[Guests] >= 1");
                    table.CheckConstraint("CK_Reservations_Total", "[TotalAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_Reservations_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalSchema: "catalog",
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_Users_GuestId",
                        column: x => x.GuestId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeasonalPricing",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false),
                    NightlyPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonalPricing", x => x.Id);
                    table.CheckConstraint("CK_SeasonalPricing_Dates", "[End] > [Start]");
                    table.ForeignKey(
                        name: "FK_SeasonalPricing_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalSchema: "catalog",
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CardLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount", "[Amount] >= 0");
                    table.ForeignKey(
                        name: "FK_Payments_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalSchema: "booking",
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReservationNights",
                schema: "booking",
                columns: table => new
                {
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Night = table.Column<DateOnly>(type: "date", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationNights", x => new { x.ReservationId, x.Night });
                    table.ForeignKey(
                        name: "FK_ReservationNights_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalSchema: "booking",
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReservationStatusHistory",
                schema: "booking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    From = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    To = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationStatusHistory_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalSchema: "booking",
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Overall = table.Column<int>(type: "int", nullable: false),
                    Cleanliness = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<int>(type: "int", nullable: false),
                    Communication = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<int>(type: "int", nullable: false),
                    CheckIn = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ModerationNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HostResponse = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HostRespondedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.CheckConstraint("CK_Reviews_Accuracy", "[Accuracy] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Reviews_CheckIn", "[CheckIn] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Reviews_Cleanliness", "[Cleanliness] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Reviews_Communication", "[Communication] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Reviews_Location", "[Location] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Reviews_Overall", "[Overall] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Reviews_Value", "[Value] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_Reviews_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalSchema: "booking",
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalSchema: "payments",
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderRefundId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.CheckConstraint("CK_Refunds_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_Refunds_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalSchema: "payments",
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Amenities",
                columns: new[] { "Code", "Category", "Icon", "Name" },
                values: new object[,]
                {
                    { "air-conditioning", "Essentials", "snowflake", "Air conditioning" },
                    { "bbq", "Outdoor", "beef", "BBQ grill" },
                    { "beachfront", "Location", "umbrella", "Beachfront" },
                    { "crib", "Family", "baby", "Crib" },
                    { "dryer", "Essentials", "wind", "Dryer" },
                    { "elevator", "Facilities", "arrow-up-down", "Elevator" },
                    { "ev-charger", "Parking", "plug-zap", "EV charger" },
                    { "fireplace", "Facilities", "flame-kindling", "Indoor fireplace" },
                    { "first-aid", "Safety", "cross", "First aid kit" },
                    { "garden", "Outdoor", "trees", "Garden" },
                    { "gym", "Facilities", "dumbbell", "Gym" },
                    { "heating", "Essentials", "flame", "Heating" },
                    { "high-chair", "Family", "baby", "High chair" },
                    { "hot-tub", "Outdoor", "bath", "Hot tub" },
                    { "kitchen", "Essentials", "chef-hat", "Kitchen" },
                    { "lake-access", "Location", "sailboat", "Lake access" },
                    { "parking", "Parking", "car", "Free parking" },
                    { "pool", "Outdoor", "waves", "Pool" },
                    { "self-check-in", "Services", "key-round", "Self check-in" },
                    { "ski-in-out", "Location", "mountain-snow", "Ski-in/ski-out" },
                    { "smoke-alarm", "Safety", "siren", "Smoke alarm" },
                    { "step-free", "Accessibility", "accessibility", "Step-free access" },
                    { "tv", "Essentials", "tv", "TV" },
                    { "washer", "Essentials", "washing-machine", "Washing machine" },
                    { "wifi", "Essentials", "wifi", "Wi-Fi" },
                    { "workspace", "Essentials", "laptop", "Dedicated workspace" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiPendingActions_UserId_CreatedAt",
                schema: "platform",
                table: "AiPendingActions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_At",
                schema: "platform",
                table: "AuditLogs",
                columns: new[] { "Action", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorId_At",
                schema: "platform",
                table: "AuditLogs",
                columns: new[] { "ActorId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_GuestId_LastMessageAt",
                schema: "engagement",
                table: "Conversations",
                columns: new[] { "GuestId", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_HostId_LastMessageAt",
                schema: "engagement",
                table: "Conversations",
                columns: new[] { "HostId", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_PropertyId_GuestId",
                schema: "engagement",
                table: "Conversations",
                columns: new[] { "PropertyId", "GuestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Code",
                schema: "pricing",
                table: "Coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_PropertyId",
                schema: "engagement",
                table: "Favorites",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_FraudChecks_SubjectId_RuleCode_CreatedAt",
                schema: "trust",
                table: "FraudChecks",
                columns: new[] { "SubjectId", "RuleCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAt",
                schema: "platform",
                table: "IdempotencyRecords",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_HostId_OccurredAt",
                schema: "payments",
                table: "LedgerEntries",
                columns: new[] { "HostId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ReservationId",
                schema: "payments",
                table: "LedgerEntries",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_SentAt",
                schema: "engagement",
                table: "Messages",
                columns: new[] { "ConversationId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAt",
                schema: "engagement",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                schema: "platform",
                table: "OutboxMessages",
                column: "OccurredAt",
                filter: "[ProcessedAt] IS NULL AND [FailedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PayerId_IdempotencyKey",
                schema: "payments",
                table: "Payments",
                columns: new[] { "PayerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Provider_ProviderPaymentId",
                schema: "payments",
                table: "Payments",
                columns: new[] { "Provider", "ProviderPaymentId" },
                unique: true,
                filter: "[ProviderPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_CreatedAt",
                schema: "payments",
                table: "Payments",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Payments_LiveReservation",
                schema: "payments",
                table: "Payments",
                column: "ReservationId",
                unique: true,
                filter: "[Status] IN ('Pending','Succeeded','PartiallyRefunded','Refunded')");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_PaymentId",
                schema: "payments",
                table: "PaymentTransactions",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_CountryCode_City",
                schema: "catalog",
                table: "Properties",
                columns: new[] { "CountryCode", "City" });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_HostId",
                schema: "catalog",
                table: "Properties",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Latitude_Longitude",
                schema: "catalog",
                table: "Properties",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Status_BasePrice",
                schema: "catalog",
                table: "Properties",
                columns: new[] { "Status", "BasePrice" });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Status_PublishedAt",
                schema: "catalog",
                table: "Properties",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Status_RatingAverage",
                schema: "catalog",
                table: "Properties",
                columns: new[] { "Status", "RatingAverage" });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAmenities_AmenityCode",
                schema: "catalog",
                table: "PropertyAmenities",
                column: "AmenityCode");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyImages_PropertyId_SortOrder",
                schema: "catalog",
                table: "PropertyImages",
                columns: new[] { "PropertyId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                schema: "identity",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_FamilyId",
                schema: "identity",
                table: "RefreshTokens",
                columns: new[] { "UserId", "FamilyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PaymentId",
                schema: "payments",
                table: "Refunds",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Status_CreatedAt",
                schema: "trust",
                table: "Reports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_ReservationNights_ActivePropertyNight",
                schema: "booking",
                table: "ReservationNights",
                columns: new[] { "PropertyId", "Night" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_GuestId_CheckIn",
                schema: "booking",
                table: "Reservations",
                columns: new[] { "GuestId", "CheckIn" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_HostId_CheckIn",
                schema: "booking",
                table: "Reservations",
                columns: new[] { "HostId", "CheckIn" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PropertyId_Status",
                schema: "booking",
                table: "Reservations",
                columns: new[] { "PropertyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status_HoldExpiresAt",
                schema: "booking",
                table: "Reservations",
                columns: new[] { "Status", "HoldExpiresAt" },
                filter: "[Status] IN ('Held','PaymentPending')");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationStatusHistory_ReservationId",
                schema: "booking",
                table: "ReservationStatusHistory",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_PropertyId_Status_CreatedAt",
                schema: "reviews",
                table: "Reviews",
                columns: new[] { "PropertyId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ReservationId",
                schema: "reviews",
                table: "Reviews",
                column: "ReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonalPricing_PropertyId_Start",
                schema: "pricing",
                table: "SeasonalPricing",
                columns: new[] { "PropertyId", "Start" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Status_Priority",
                schema: "trust",
                table: "SupportTickets",
                columns: new[] { "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_UserId",
                schema: "trust",
                table: "SupportTickets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TicketId",
                schema: "trust",
                table: "TicketMessages",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                schema: "identity",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTokens_UserId_Purpose_TokenHash",
                schema: "identity",
                table: "UserTokens",
                columns: new[] { "UserId", "Purpose", "TokenHash" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_Provider_EventId",
                schema: "payments",
                table: "WebhookEvents",
                columns: new[] { "Provider", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiPendingActions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "BlockedDates",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "Coupons",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "Favorites",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "FraudChecks",
                schema: "trust");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "LedgerEntries",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Messages",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "PaymentTransactions",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "PropertyAmenities",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "PropertyImages",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Refunds",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Reports",
                schema: "trust");

            migrationBuilder.DropTable(
                name: "ReservationNights",
                schema: "booking");

            migrationBuilder.DropTable(
                name: "ReservationStatusHistory",
                schema: "booking");

            migrationBuilder.DropTable(
                name: "Reviews",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "SeasonalPricing",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "TicketMessages",
                schema: "trust");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "WebhookEvents",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Conversations",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "Amenities",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Payments",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "SupportTickets",
                schema: "trust");

            migrationBuilder.DropTable(
                name: "Reservations",
                schema: "booking");

            migrationBuilder.DropTable(
                name: "Properties",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "identity");
        }
    }
}

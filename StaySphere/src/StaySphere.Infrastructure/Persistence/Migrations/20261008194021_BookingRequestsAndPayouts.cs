using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaySphere.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BookingRequestsAndPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Payments_LiveReservation",
                schema: "payments",
                table: "Payments");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovalDeadline",
                schema: "booking",
                table: "Reservations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                schema: "booking",
                table: "Reservations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresApproval",
                schema: "booking",
                table: "Reservations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "HostPayouts",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProviderPayoutId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Automatic = table.Column<bool>(type: "bit", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostPayouts", x => x.Id);
                    table.CheckConstraint("CK_HostPayouts_Amount", "[Amount] > 0");
                });

            migrationBuilder.CreateTable(
                name: "PayoutAccounts",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountHolder = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaskedAccount = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayoutAccounts_Users_HostId",
                        column: x => x.HostId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HostPayoutItems",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostPayoutItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostPayoutItems_HostPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalSchema: "payments",
                        principalTable: "HostPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status_ApprovalDeadline",
                schema: "booking",
                table: "Reservations",
                columns: new[] { "Status", "ApprovalDeadline" },
                filter: "[Status] = 'AwaitingApproval'");

            migrationBuilder.CreateIndex(
                name: "UX_Payments_LiveReservation",
                schema: "payments",
                table: "Payments",
                column: "ReservationId",
                unique: true,
                filter: "[Status] IN ('Pending','Authorized','Succeeded','PartiallyRefunded','Refunded')");

            migrationBuilder.CreateIndex(
                name: "IX_HostPayoutItems_PayoutId",
                schema: "payments",
                table: "HostPayoutItems",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_HostPayoutItems_ReservationId",
                schema: "payments",
                table: "HostPayoutItems",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_HostPayouts_HostId_CreatedAt",
                schema: "payments",
                table: "HostPayouts",
                columns: new[] { "HostId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_HostPayouts_OneProcessing",
                schema: "payments",
                table: "HostPayouts",
                columns: new[] { "HostId", "Currency" },
                unique: true,
                filter: "[Status] = 'Processing'");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutAccounts_HostId",
                schema: "payments",
                table: "PayoutAccounts",
                column: "HostId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostPayoutItems",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "PayoutAccounts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "HostPayouts",
                schema: "payments");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_Status_ApprovalDeadline",
                schema: "booking",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "UX_Payments_LiveReservation",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ApprovalDeadline",
                schema: "booking",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DeclineReason",
                schema: "booking",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RequiresApproval",
                schema: "booking",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "UX_Payments_LiveReservation",
                schema: "payments",
                table: "Payments",
                column: "ReservationId",
                unique: true,
                filter: "[Status] IN ('Pending','Succeeded','PartiallyRefunded','Refunded')");
        }
    }
}

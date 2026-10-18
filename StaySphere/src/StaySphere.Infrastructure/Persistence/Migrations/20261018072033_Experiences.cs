using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaySphere.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Experiences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "experiences");

            migrationBuilder.CreateTable(
                name: "Experiences",
                schema: "experiences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CountryCode = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    MeetingPoint = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxGuests = table.Column<int>(type: "int", nullable: false),
                    PricePerGuest = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experiences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Experiences_Users_HostId",
                        column: x => x.HostId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExperienceSlots",
                schema: "experiences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExperienceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    Booked = table.Column<int>(type: "int", nullable: false),
                    Cancelled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienceSlots", x => x.Id);
                    table.CheckConstraint("CK_ExperienceSlots_Booked", "[Booked] >= 0 AND [Booked] <= [Capacity]");
                    table.ForeignKey(
                        name: "FK_ExperienceSlots_Experiences_ExperienceId",
                        column: x => x.ExperienceId,
                        principalSchema: "experiences",
                        principalTable: "Experiences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExperienceBookings",
                schema: "experiences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExperienceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Guests = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienceBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperienceBookings_ExperienceSlots_SlotId",
                        column: x => x.SlotId,
                        principalSchema: "experiences",
                        principalTable: "ExperienceSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExperienceBookings_Users_GuestId",
                        column: x => x.GuestId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceBookings_GuestId_StartsAt",
                schema: "experiences",
                table: "ExperienceBookings",
                columns: new[] { "GuestId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceBookings_SlotId",
                schema: "experiences",
                table: "ExperienceBookings",
                column: "SlotId");

            migrationBuilder.CreateIndex(
                name: "IX_Experiences_HostId",
                schema: "experiences",
                table: "Experiences",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_Experiences_Status_City",
                schema: "experiences",
                table: "Experiences",
                columns: new[] { "Status", "City" });

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceSlots_ExperienceId_StartsAt",
                schema: "experiences",
                table: "ExperienceSlots",
                columns: new[] { "ExperienceId", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExperienceBookings",
                schema: "experiences");

            migrationBuilder.DropTable(
                name: "ExperienceSlots",
                schema: "experiences");

            migrationBuilder.DropTable(
                name: "Experiences",
                schema: "experiences");
        }
    }
}

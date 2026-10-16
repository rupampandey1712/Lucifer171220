using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaySphere.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Promotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Promotions",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PercentOff = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    HostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MinNights = table.Column<int>(type: "int", nullable: true),
                    DaysBeforeCheckIn = table.Column<int>(type: "int", nullable: true),
                    StayStart = table.Column<DateOnly>(type: "date", nullable: true),
                    StayEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.CheckConstraint("CK_Promotions_Percent", "[PercentOff] > 0 AND [PercentOff] <= 50");
                    table.CheckConstraint("CK_Promotions_Window", "[ValidTo] > [ValidFrom]");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_Active",
                schema: "pricing",
                table: "Promotions",
                columns: new[] { "HostId", "PropertyId" },
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Promotions",
                schema: "pricing");
        }
    }
}

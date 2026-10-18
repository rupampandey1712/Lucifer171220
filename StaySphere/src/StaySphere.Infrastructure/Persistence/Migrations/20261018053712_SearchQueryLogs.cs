using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaySphere.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchQueryLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchQueryLogs",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Guests = table.Column<int>(type: "int", nullable: true),
                    Nights = table.Column<int>(type: "int", nullable: true),
                    ResultCount = table.Column<int>(type: "int", nullable: false),
                    SearchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchQueryLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueryLogs_SearchedAt",
                schema: "catalog",
                table: "SearchQueryLogs",
                column: "SearchedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchQueryLogs",
                schema: "catalog");
        }
    }
}

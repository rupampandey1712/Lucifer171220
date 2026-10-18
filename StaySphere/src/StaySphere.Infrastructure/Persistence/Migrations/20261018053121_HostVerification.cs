using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaySphere.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HostVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HostVerification",
                schema: "identity",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "HostVerificationLegalName",
                schema: "identity",
                table: "Users",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HostVerificationNote",
                schema: "identity",
                table: "Users",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HostVerificationSubmittedAt",
                schema: "identity",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HostVerifiedAt",
                schema: "identity",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_HostVerification",
                schema: "identity",
                table: "Users",
                column: "HostVerification",
                filter: "[HostVerification] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_HostVerification",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HostVerification",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HostVerificationLegalName",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HostVerificationNote",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HostVerificationSubmittedAt",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HostVerifiedAt",
                schema: "identity",
                table: "Users");
        }
    }
}

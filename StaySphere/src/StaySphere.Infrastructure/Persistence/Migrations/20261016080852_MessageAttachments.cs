using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaySphere.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MessageAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                schema: "engagement",
                table: "Messages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentKey",
                schema: "engagement",
                table: "Messages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentName",
                schema: "engagement",
                table: "Messages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AttachmentSize",
                schema: "engagement",
                table: "Messages",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                schema: "engagement",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AttachmentKey",
                schema: "engagement",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AttachmentName",
                schema: "engagement",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AttachmentSize",
                schema: "engagement",
                table: "Messages");
        }
    }
}

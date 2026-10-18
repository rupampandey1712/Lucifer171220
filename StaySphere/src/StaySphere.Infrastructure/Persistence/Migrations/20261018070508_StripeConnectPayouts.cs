using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaySphere.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StripeConnectPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PayoutsEnabled",
                schema: "payments",
                table: "PayoutAccounts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderAccountId",
                schema: "payments",
                table: "PayoutAccounts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayoutsEnabled",
                schema: "payments",
                table: "PayoutAccounts");

            migrationBuilder.DropColumn(
                name: "ProviderAccountId",
                schema: "payments",
                table: "PayoutAccounts");
        }
    }
}

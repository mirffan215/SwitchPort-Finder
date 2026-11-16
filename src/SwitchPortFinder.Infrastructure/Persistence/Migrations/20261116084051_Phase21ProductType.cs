using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase21ProductType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductType",
                table: "Switches",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Switch");

            // controllers flagged with the old checkbox keep working
            migrationBuilder.Sql("UPDATE Switches SET ProductType = 'WirelessController' WHERE IsWirelessController = 1");

            migrationBuilder.DropColumn(
                name: "IsWirelessController",
                table: "Switches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWirelessController",
                table: "Switches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE Switches SET IsWirelessController = 1 WHERE ProductType = 'WirelessController'");

            migrationBuilder.DropColumn(
                name: "ProductType",
                table: "Switches");
        }
    }
}

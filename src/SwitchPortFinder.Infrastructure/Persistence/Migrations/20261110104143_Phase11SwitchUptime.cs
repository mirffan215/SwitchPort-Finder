using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase11SwitchUptime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UptimeAt",
                table: "Switches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptimeSeconds",
                table: "Switches",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UptimeAt",
                table: "Switches");

            migrationBuilder.DropColumn(
                name: "UptimeSeconds",
                table: "Switches");
        }
    }
}

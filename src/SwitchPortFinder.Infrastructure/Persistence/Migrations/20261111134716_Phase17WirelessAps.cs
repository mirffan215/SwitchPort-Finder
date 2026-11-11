using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase17WirelessAps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWirelessController",
                table: "Switches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WirelessAps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ControllerId = table.Column<int>(type: "int", nullable: false),
                    SysMac = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    EthMac = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Serial = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UptimeSeconds = table.Column<long>(type: "bigint", nullable: true),
                    Clients = table.Column<int>(type: "int", nullable: true),
                    AdminEnabled = table.Column<bool>(type: "bit", nullable: true),
                    Associated = table.Column<bool>(type: "bit", nullable: true),
                    FirstSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WirelessAps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WirelessAps_Switches_ControllerId",
                        column: x => x.ControllerId,
                        principalTable: "Switches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WirelessAps_ControllerId_SysMac",
                table: "WirelessAps",
                columns: new[] { "ControllerId", "SysMac" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WirelessAps_EthMac",
                table: "WirelessAps",
                column: "EthMac");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WirelessAps");

            migrationBuilder.DropColumn(
                name: "IsWirelessController",
                table: "Switches");
        }
    }
}

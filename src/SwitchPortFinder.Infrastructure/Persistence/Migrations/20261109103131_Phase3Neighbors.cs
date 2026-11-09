using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Neighbors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Confidence",
                table: "SwitchPorts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "NeighborId",
                table: "SwitchPorts",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Neighbors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SwitchId = table.Column<int>(type: "int", nullable: false),
                    PortId = table.Column<int>(type: "int", nullable: false),
                    Protocol = table.Column<int>(type: "int", nullable: false),
                    RemoteKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ChassisId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ChassisMac = table.Column<string>(type: "nchar(12)", fixedLength: true, maxLength: 12, nullable: true),
                    RemotePortId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RemotePortDescr = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SysName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SysDescr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Caps = table.Column<int>(type: "int", nullable: false),
                    MgmtIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    MatchedSwitchId = table.Column<int>(type: "int", nullable: true),
                    Evidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FirstSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Neighbors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Neighbors_SwitchPorts_PortId",
                        column: x => x.PortId,
                        principalTable: "SwitchPorts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Neighbors_Switches_SwitchId",
                        column: x => x.SwitchId,
                        principalTable: "Switches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SwitchMacs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SwitchId = table.Column<int>(type: "int", nullable: false),
                    Mac = table.Column<string>(type: "nchar(12)", fixedLength: true, maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SwitchMacs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SwitchMacs_Switches_SwitchId",
                        column: x => x.SwitchId,
                        principalTable: "Switches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Neighbors_MatchedSwitchId",
                table: "Neighbors",
                column: "MatchedSwitchId");

            migrationBuilder.CreateIndex(
                name: "IX_Neighbors_PortId_Protocol_RemoteKey",
                table: "Neighbors",
                columns: new[] { "PortId", "Protocol", "RemoteKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Neighbors_SwitchId",
                table: "Neighbors",
                column: "SwitchId");

            migrationBuilder.CreateIndex(
                name: "IX_SwitchMacs_Mac",
                table: "SwitchMacs",
                column: "Mac");

            migrationBuilder.CreateIndex(
                name: "IX_SwitchMacs_SwitchId_Mac",
                table: "SwitchMacs",
                columns: new[] { "SwitchId", "Mac" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Neighbors");

            migrationBuilder.DropTable(
                name: "SwitchMacs");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "NeighborId",
                table: "SwitchPorts");
        }
    }
}

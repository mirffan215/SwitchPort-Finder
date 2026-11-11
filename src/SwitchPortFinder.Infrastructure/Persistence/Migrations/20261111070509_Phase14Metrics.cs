using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase14Metrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CpuPct",
                table: "Switches",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HealthAt",
                table: "Switches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LatencyMs",
                table: "Switches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MemPct",
                table: "Switches",
                type: "float",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PortMetrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SwitchId = table.Column<int>(type: "int", nullable: false),
                    PortId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InBps = table.Column<double>(type: "float", nullable: true),
                    OutBps = table.Column<double>(type: "float", nullable: true),
                    UtilPct = table.Column<double>(type: "float", nullable: true),
                    DiscardsPerSec = table.Column<double>(type: "float", nullable: false),
                    ErrorsPerSec = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SwitchMetrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SwitchId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reachable = table.Column<bool>(type: "bit", nullable: false),
                    UptimeSeconds = table.Column<long>(type: "bigint", nullable: true),
                    CpuPct = table.Column<double>(type: "float", nullable: true),
                    MemPct = table.Column<double>(type: "float", nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SwitchMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SwitchMetrics_Switches_SwitchId",
                        column: x => x.SwitchId,
                        principalTable: "Switches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortMetrics_At",
                table: "PortMetrics",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_PortMetrics_PortId_At",
                table: "PortMetrics",
                columns: new[] { "PortId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_PortMetrics_SwitchId_At",
                table: "PortMetrics",
                columns: new[] { "SwitchId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Name",
                table: "Reports",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_SwitchMetrics_At",
                table: "SwitchMetrics",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_SwitchMetrics_SwitchId_At",
                table: "SwitchMetrics",
                columns: new[] { "SwitchId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortMetrics");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "SwitchMetrics");

            migrationBuilder.DropColumn(
                name: "CpuPct",
                table: "Switches");

            migrationBuilder.DropColumn(
                name: "HealthAt",
                table: "Switches");

            migrationBuilder.DropColumn(
                name: "LatencyMs",
                table: "Switches");

            migrationBuilder.DropColumn(
                name: "MemPct",
                table: "Switches");
        }
    }
}

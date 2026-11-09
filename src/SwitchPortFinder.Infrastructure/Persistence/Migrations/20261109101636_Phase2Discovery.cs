using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Discovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ArpAutoDetected",
                table: "Switches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "Switches",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArpEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceSwitchId = table.Column<int>(type: "int", nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    Mac = table.Column<string>(type: "nchar(12)", fixedLength: true, maxLength: 12, nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArpEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArpEntries_Switches_SourceSwitchId",
                        column: x => x.SourceSwitchId,
                        principalTable: "Switches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SwitchId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StepsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PortsFound = table.Column<int>(type: "int", nullable: false),
                    DevicesFound = table.Column<int>(type: "int", nullable: false),
                    NeighborsFound = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveryJobs_Switches_SwitchId",
                        column: x => x.SwitchId,
                        principalTable: "Switches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SwitchPorts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SwitchId = table.Column<int>(type: "int", nullable: false),
                    IfIndex = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descr = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Alias = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    AdminUp = table.Column<bool>(type: "bit", nullable: false),
                    OperUp = table.Column<bool>(type: "bit", nullable: false),
                    SpeedMbps = table.Column<long>(type: "bigint", nullable: false),
                    PhysAddress = table.Column<string>(type: "nchar(12)", fixedLength: true, maxLength: 12, nullable: true),
                    Mtu = table.Column<int>(type: "int", nullable: false),
                    Vlan = table.Column<int>(type: "int", nullable: true),
                    PortType = table.Column<int>(type: "int", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SwitchPorts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SwitchPorts_Switches_SwitchId",
                        column: x => x.SwitchId,
                        principalTable: "Switches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryErrors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveryErrors_DiscoveryJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "DiscoveryJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Mac = table.Column<string>(type: "nchar(12)", fixedLength: true, maxLength: 12, nullable: false),
                    Hostname = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    HostnameSource = table.Column<int>(type: "int", nullable: false),
                    HostnameCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    IpSourceSwitchId = table.Column<int>(type: "int", nullable: true),
                    IpObservedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SwitchId = table.Column<int>(type: "int", nullable: true),
                    PortId = table.Column<int>(type: "int", nullable: true),
                    Vlan = table.Column<int>(type: "int", nullable: true),
                    LowConfidence = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_SwitchPorts_PortId",
                        column: x => x.PortId,
                        principalTable: "SwitchPorts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Devices_Switches_SwitchId",
                        column: x => x.SwitchId,
                        principalTable: "Switches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Observations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Mac = table.Column<string>(type: "nchar(12)", fixedLength: true, maxLength: 12, nullable: false),
                    SwitchId = table.Column<int>(type: "int", nullable: false),
                    PortId = table.Column<int>(type: "int", nullable: false),
                    Vlan = table.Column<int>(type: "int", nullable: true),
                    IsStatic = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Observations_SwitchPorts_PortId",
                        column: x => x.PortId,
                        principalTable: "SwitchPorts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Observations_Switches_SwitchId",
                        column: x => x.SwitchId,
                        principalTable: "Switches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArpEntries_Mac",
                table: "ArpEntries",
                column: "Mac");

            migrationBuilder.CreateIndex(
                name: "IX_ArpEntries_SourceSwitchId_Ip",
                table: "ArpEntries",
                columns: new[] { "SourceSwitchId", "Ip" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Hostname",
                table: "Devices",
                column: "Hostname");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_IpAddress",
                table: "Devices",
                column: "IpAddress");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_LastSeen",
                table: "Devices",
                column: "LastSeen");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Mac",
                table: "Devices",
                column: "Mac",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_PortId",
                table: "Devices",
                column: "PortId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_SwitchId",
                table: "Devices",
                column: "SwitchId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_SwitchId_LastSeen",
                table: "Devices",
                columns: new[] { "SwitchId", "LastSeen" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryErrors_JobId",
                table: "DiscoveryErrors",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryJobs_QueuedAt",
                table: "DiscoveryJobs",
                column: "QueuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryJobs_SwitchId_Status",
                table: "DiscoveryJobs",
                columns: new[] { "SwitchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_LastSeen",
                table: "Observations",
                column: "LastSeen");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_Mac",
                table: "Observations",
                column: "Mac");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_PortId",
                table: "Observations",
                column: "PortId");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_SwitchId_Mac",
                table: "Observations",
                columns: new[] { "SwitchId", "Mac" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SwitchPorts_Name",
                table: "SwitchPorts",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_SwitchPorts_SwitchId_IfIndex",
                table: "SwitchPorts",
                columns: new[] { "SwitchId", "IfIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArpEntries");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "DiscoveryErrors");

            migrationBuilder.DropTable(
                name: "Observations");

            migrationBuilder.DropTable(
                name: "DiscoveryJobs");

            migrationBuilder.DropTable(
                name: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "ArpAutoDetected",
                table: "Switches");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "Switches");
        }
    }
}

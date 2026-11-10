using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8PortCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CtrAt",
                table: "SwitchPorts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CtrInDiscards",
                table: "SwitchPorts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CtrInErrors",
                table: "SwitchPorts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CtrInOctets",
                table: "SwitchPorts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CtrOutDiscards",
                table: "SwitchPorts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CtrOutErrors",
                table: "SwitchPorts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CtrOutOctets",
                table: "SwitchPorts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CtrUpTicks",
                table: "SwitchPorts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "CtrWide",
                table: "SwitchPorts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RateAt",
                table: "SwitchPorts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RateDiscardsPerSec",
                table: "SwitchPorts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RateErrorsPerSec",
                table: "SwitchPorts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RateInBps",
                table: "SwitchPorts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RateOutBps",
                table: "SwitchPorts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateSeconds",
                table: "SwitchPorts",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CtrAt",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "CtrInDiscards",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "CtrInErrors",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "CtrInOctets",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "CtrOutDiscards",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "CtrOutErrors",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "CtrOutOctets",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "CtrUpTicks",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "CtrWide",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "RateAt",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "RateDiscardsPerSec",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "RateErrorsPerSec",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "RateInBps",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "RateOutBps",
                table: "SwitchPorts");

            migrationBuilder.DropColumn(
                name: "RateSeconds",
                table: "SwitchPorts");
        }
    }
}

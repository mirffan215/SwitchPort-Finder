using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase13DeviceManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "Devices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManualUpdatedAt",
                table: "Devices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualUpdatedBy",
                table: "Devices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Devices",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Owner",
                table: "Devices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinnedPortId",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinnedSwitchId",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinnedVlan",
                table: "Devices",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ManualUpdatedAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ManualUpdatedBy",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Owner",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "PinnedPortId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "PinnedSwitchId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "PinnedVlan",
                table: "Devices");
        }
    }
}

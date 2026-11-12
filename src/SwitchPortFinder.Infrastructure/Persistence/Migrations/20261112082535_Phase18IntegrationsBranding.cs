using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase18IntegrationsBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExtAt",
                table: "Devices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtDetail",
                table: "Devices",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtOs",
                table: "Devices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtSource",
                table: "Devices",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtUser",
                table: "Devices",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Brandings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LinkUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LogoBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    LogoContentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brandings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Integrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastOk = table.Column<bool>(type: "bit", nullable: true),
                    LastMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastMatched = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Integrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_Kind",
                table: "Integrations",
                column: "Kind",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Brandings");

            migrationBuilder.DropTable(
                name: "Integrations");

            migrationBuilder.DropColumn(
                name: "ExtAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ExtDetail",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ExtOs",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ExtSource",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ExtUser",
                table: "Devices");
        }
    }
}

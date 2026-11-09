using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PhysAddress",
                table: "SwitchPorts",
                type: "char(12)",
                unicode: false,
                fixedLength: true,
                maxLength: 12,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nchar(12)",
                oldFixedLength: true,
                oldMaxLength: 12,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mac",
                table: "SwitchMacs",
                type: "char(12)",
                unicode: false,
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nchar(12)",
                oldFixedLength: true,
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<string>(
                name: "Mac",
                table: "Observations",
                type: "char(12)",
                unicode: false,
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nchar(12)",
                oldFixedLength: true,
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<string>(
                name: "ChassisMac",
                table: "Neighbors",
                type: "char(12)",
                unicode: false,
                fixedLength: true,
                maxLength: 12,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nchar(12)",
                oldFixedLength: true,
                oldMaxLength: 12,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mac",
                table: "Devices",
                type: "char(12)",
                unicode: false,
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nchar(12)",
                oldFixedLength: true,
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<string>(
                name: "Mac",
                table: "ArpEntries",
                type: "char(12)",
                unicode: false,
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nchar(12)",
                oldFixedLength: true,
                oldMaxLength: 12);

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.AlterColumn<string>(
                name: "PhysAddress",
                table: "SwitchPorts",
                type: "nchar(12)",
                fixedLength: true,
                maxLength: 12,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "char(12)",
                oldUnicode: false,
                oldFixedLength: true,
                oldMaxLength: 12,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mac",
                table: "SwitchMacs",
                type: "nchar(12)",
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(12)",
                oldUnicode: false,
                oldFixedLength: true,
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<string>(
                name: "Mac",
                table: "Observations",
                type: "nchar(12)",
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(12)",
                oldUnicode: false,
                oldFixedLength: true,
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<string>(
                name: "ChassisMac",
                table: "Neighbors",
                type: "nchar(12)",
                fixedLength: true,
                maxLength: 12,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "char(12)",
                oldUnicode: false,
                oldFixedLength: true,
                oldMaxLength: 12,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mac",
                table: "Devices",
                type: "nchar(12)",
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(12)",
                oldUnicode: false,
                oldFixedLength: true,
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<string>(
                name: "Mac",
                table: "ArpEntries",
                type: "nchar(12)",
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(12)",
                oldUnicode: false,
                oldFixedLength: true,
                oldMaxLength: 12);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase20MfaDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MfaPendingName",
                table: "Users",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MfaDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SecretProtected = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastStep = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfaDevices_UserId",
                table: "MfaDevices",
                column: "UserId");

            // existing accounts keep working: their single authenticator becomes the first device
            migrationBuilder.Sql("INSERT INTO MfaDevices (UserId, Name, SecretProtected, LastStep, CreatedAt) SELECT Id, 'Authenticator 1', MfaSecretProtected, MfaLastStep, CURRENT_TIMESTAMP FROM Users WHERE MfaEnabled = 1 AND MfaSecretProtected IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaDevices");

            migrationBuilder.DropColumn(
                name: "MfaPendingName",
                table: "Users");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CredentialProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    AuthProtocol = table.Column<int>(type: "int", nullable: false),
                    PrivProtocol = table.Column<int>(type: "int", nullable: false),
                    ContextName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EncryptedSecrets = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    CreatedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Switches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ManagementIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    Manufacturer = table.Column<int>(type: "int", nullable: false),
                    ManufacturerOther = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DetectedManufacturer = table.Column<int>(type: "int", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UseAsArpSource = table.Column<bool>(type: "bit", nullable: false),
                    SysObjectId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SysName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SysDescr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastScanAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CredentialProfileId = table.Column<int>(type: "int", nullable: true),
                    ImportBatchId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Switches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Switches_CredentialProfiles_CredentialProfileId",
                        column: x => x.CredentialProfileId,
                        principalTable: "CredentialProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Switches_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SwitchCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SwitchId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    AuthProtocol = table.Column<int>(type: "int", nullable: false),
                    PrivProtocol = table.Column<int>(type: "int", nullable: false),
                    ContextName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EncryptedSecrets = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SwitchCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SwitchCredentials_Switches_SwitchId",
                        column: x => x.SwitchId,
                        principalTable: "Switches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CredentialProfiles_Name",
                table: "CredentialProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SwitchCredentials_SwitchId",
                table: "SwitchCredentials",
                column: "SwitchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Switches_CredentialProfileId",
                table: "Switches",
                column: "CredentialProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Switches_ImportBatchId",
                table: "Switches",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Switches_ManagementIp",
                table: "Switches",
                column: "ManagementIp",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Switches_Name",
                table: "Switches",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SwitchCredentials");

            migrationBuilder.DropTable(
                name: "Switches");

            migrationBuilder.DropTable(
                name: "CredentialProfiles");

            migrationBuilder.DropTable(
                name: "ImportBatches");
        }
    }
}

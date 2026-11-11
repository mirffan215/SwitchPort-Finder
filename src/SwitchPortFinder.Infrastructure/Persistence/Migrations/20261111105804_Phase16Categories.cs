using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase16Categories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Devices",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unclassified");   // existing devices: a valid enum name (the generated empty string would not parse)

            migrationBuilder.AddColumn<bool>(
                name: "CategoryManual",
                table: "Devices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CategoryReason",
                table: "Devices",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoryRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    VendorContains = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HostnameStartsWith = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HostnameContains = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PortDescriptionContains = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NeighborContains = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NeighborCapability = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Vlan = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Category",
                table: "Devices",
                column: "Category");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryRules");

            migrationBuilder.DropIndex(
                name: "IX_Devices_Category",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "CategoryManual",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "CategoryReason",
                table: "Devices");
        }
    }
}

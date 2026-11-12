using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwitchPortFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase19DhcpDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DhcpDetail",
                table: "Devices",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DhcpDetail",
                table: "Devices");
        }
    }
}

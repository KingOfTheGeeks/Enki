using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Master
{
    /// <inheritdoc />
    public partial class RemoveTenantUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "TenantUser");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "TenantUser",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}

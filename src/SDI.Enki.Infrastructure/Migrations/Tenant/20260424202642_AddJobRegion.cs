using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddJobRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "Job",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Job_Region",
                table: "Job",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_Job_Status",
                table: "Job",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Job_Region",
                table: "Job");

            migrationBuilder.DropIndex(
                name: "IX_Job_Status",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "Job");
        }
    }
}

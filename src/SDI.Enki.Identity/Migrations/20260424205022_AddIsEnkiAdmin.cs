using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddIsEnkiAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnkiAdmin",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEnkiAdmin",
                table: "AspNetUsers");
        }
    }
}

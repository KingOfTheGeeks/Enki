using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddUserClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TeamSubtype",
                table: "AspNetUsers",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeamSubtype",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");
        }
    }
}

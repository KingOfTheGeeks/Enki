using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Identity.Migrations
{
    /// <inheritdoc />
    public partial class SessionLifetime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SessionLifetimeMinutes",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SessionLifetimeUpdatedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionLifetimeUpdatedBy",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionLifetimeMinutes",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SessionLifetimeUpdatedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SessionLifetimeUpdatedBy",
                table: "AspNetUsers");
        }
    }
}

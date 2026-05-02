using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Master
{
    /// <inheritdoc />
    public partial class AddToolRetirementColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Disposition",
                table: "Tool",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacementToolId",
                table: "Tool",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                table: "Tool",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetiredBy",
                table: "Tool",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetirementLocation",
                table: "Tool",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetirementReason",
                table: "Tool",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tool_Disposition",
                table: "Tool",
                column: "Disposition");

            migrationBuilder.CreateIndex(
                name: "IX_Tool_ReplacementToolId",
                table: "Tool",
                column: "ReplacementToolId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tool_Tool_ReplacementToolId",
                table: "Tool",
                column: "ReplacementToolId",
                principalTable: "Tool",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tool_Tool_ReplacementToolId",
                table: "Tool");

            migrationBuilder.DropIndex(
                name: "IX_Tool_Disposition",
                table: "Tool");

            migrationBuilder.DropIndex(
                name: "IX_Tool_ReplacementToolId",
                table: "Tool");

            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "Tool");

            migrationBuilder.DropColumn(
                name: "ReplacementToolId",
                table: "Tool");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "Tool");

            migrationBuilder.DropColumn(
                name: "RetiredBy",
                table: "Tool");

            migrationBuilder.DropColumn(
                name: "RetirementLocation",
                table: "Tool");

            migrationBuilder.DropColumn(
                name: "RetirementReason",
                table: "Tool");
        }
    }
}

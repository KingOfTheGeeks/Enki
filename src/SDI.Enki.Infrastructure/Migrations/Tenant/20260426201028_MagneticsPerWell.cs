using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class MagneticsPerWell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Magnetics_BTotal_Dip_Declination",
                table: "Magnetics");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Magnetics",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Magnetics",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Magnetics",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Magnetics",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Magnetics",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WellId",
                table: "Magnetics",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Magnetics_BTotal_Dip_Declination",
                table: "Magnetics",
                columns: new[] { "BTotal", "Dip", "Declination" },
                unique: true,
                filter: "[WellId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Magnetics_WellId",
                table: "Magnetics",
                column: "WellId",
                unique: true,
                filter: "[WellId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Magnetics_Well_WellId",
                table: "Magnetics",
                column: "WellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Magnetics_Well_WellId",
                table: "Magnetics");

            migrationBuilder.DropIndex(
                name: "IX_Magnetics_BTotal_Dip_Declination",
                table: "Magnetics");

            migrationBuilder.DropIndex(
                name: "IX_Magnetics_WellId",
                table: "Magnetics");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Magnetics");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Magnetics");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Magnetics");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Magnetics");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Magnetics");

            migrationBuilder.DropColumn(
                name: "WellId",
                table: "Magnetics");

            migrationBuilder.CreateIndex(
                name: "IX_Magnetics_BTotal_Dip_Declination",
                table: "Magnetics",
                columns: new[] { "BTotal", "Dip", "Declination" },
                unique: true);
        }
    }
}

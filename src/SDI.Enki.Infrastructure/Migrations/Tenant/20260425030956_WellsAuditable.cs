using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class WellsAuditable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Well",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Well",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Well",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Well",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Well",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Tubular",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Tubular",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Tubular",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Tubular",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Tubular",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "TieOn",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "TieOn",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TieOn",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "TieOn",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "TieOn",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Survey",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Survey",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Survey",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Survey",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Survey",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Formation",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Formation",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Formation",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Formation",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Formation",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "CommonMeasure",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "CommonMeasure",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CommonMeasure",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "CommonMeasure",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "CommonMeasure",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Well");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Well");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Well");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Well");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Well");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Tubular");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Tubular");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Tubular");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Tubular");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Tubular");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "TieOn");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "TieOn");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TieOn");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "TieOn");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "TieOn");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Survey");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Survey");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Survey");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Survey");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Survey");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Formation");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Formation");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Formation");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Formation");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Formation");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "CommonMeasure");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "CommonMeasure");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CommonMeasure");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "CommonMeasure");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "CommonMeasure");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class MagneticsRowVersion : Migration
    {
        // SQL Server doesn't allow ALTER COLUMN to type rowversion / timestamp
        // (error 4927 — "Cannot alter column 'X' to be data type timestamp.").
        // EF Core's default scaffold for `IsRowVersion()` was AlterColumn,
        // which fails at runtime; the only working shape is DROP + ADD.
        // Safe here because the existing varbinary(MAX) column was never
        // populated (the bug we're fixing) — there's no data to preserve.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Magnetics");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Magnetics",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Magnetics");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Magnetics",
                type: "varbinary(max)",
                nullable: true);
        }
    }
}

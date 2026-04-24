using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddMagneticsAndCalibrationsLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Calibrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CalibrationString = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calibrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Magnetics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BTotal = table.Column<double>(type: "float", nullable: false),
                    Dip = table.Column<double>(type: "float", nullable: false),
                    Declination = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Magnetics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Calibrations_Name_CalibrationString",
                table: "Calibrations",
                columns: new[] { "Name", "CalibrationString" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Magnetics_BTotal_Dip_Declination",
                table: "Magnetics",
                columns: new[] { "BTotal", "Dip", "Declination" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Calibrations");

            migrationBuilder.DropTable(
                name: "Magnetics");
        }
    }
}

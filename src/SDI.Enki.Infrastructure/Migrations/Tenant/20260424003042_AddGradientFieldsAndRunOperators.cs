using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddGradientFieldsAndRunOperators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BridleLength",
                table: "Runs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CurrentInjection",
                table: "Runs",
                type: "float",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RunOperator",
                columns: table => new
                {
                    OperatorsId = table.Column<int>(type: "int", nullable: false),
                    RunsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunOperator", x => new { x.OperatorsId, x.RunsId });
                    table.ForeignKey(
                        name: "FK_RunOperator_Operators_OperatorsId",
                        column: x => x.OperatorsId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RunOperator_Runs_RunsId",
                        column: x => x.RunsId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunOperator_RunsId",
                table: "RunOperator",
                column: "RunsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunOperator");

            migrationBuilder.DropColumn(
                name: "BridleLength",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "CurrentInjection",
                table: "Runs");
        }
    }
}

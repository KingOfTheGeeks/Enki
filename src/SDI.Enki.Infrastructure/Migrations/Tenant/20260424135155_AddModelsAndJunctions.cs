using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddModelsAndJunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GradientModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TargetWellId = table.Column<int>(type: "int", nullable: false),
                    InjectionWellId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradientModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradientModels_Wells_InjectionWellId",
                        column: x => x.InjectionWellId,
                        principalTable: "Wells",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradientModels_Wells_TargetWellId",
                        column: x => x.TargetWellId,
                        principalTable: "Wells",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RotaryModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetWellId = table.Column<int>(type: "int", nullable: false),
                    InjectionWellId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotaryModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotaryModels_Wells_InjectionWellId",
                        column: x => x.InjectionWellId,
                        principalTable: "Wells",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RotaryModels_Wells_TargetWellId",
                        column: x => x.TargetWellId,
                        principalTable: "Wells",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradientModelRun",
                columns: table => new
                {
                    GradientModelsId = table.Column<int>(type: "int", nullable: false),
                    RunsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradientModelRun", x => new { x.GradientModelsId, x.RunsId });
                    table.ForeignKey(
                        name: "FK_GradientModelRun_GradientModels_GradientModelsId",
                        column: x => x.GradientModelsId,
                        principalTable: "GradientModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GradientModelRun_Runs_RunsId",
                        column: x => x.RunsId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedGradientModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreationTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GradientModelId = table.Column<int>(type: "int", nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SaveType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedGradientModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedGradientModels_GradientModels_GradientModelId",
                        column: x => x.GradientModelId,
                        principalTable: "GradientModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotaryModelRun",
                columns: table => new
                {
                    RotaryModelsId = table.Column<int>(type: "int", nullable: false),
                    RunsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotaryModelRun", x => new { x.RotaryModelsId, x.RunsId });
                    table.ForeignKey(
                        name: "FK_RotaryModelRun_RotaryModels_RotaryModelsId",
                        column: x => x.RotaryModelsId,
                        principalTable: "RotaryModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RotaryModelRun_Runs_RunsId",
                        column: x => x.RunsId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GradientModelRun_RunsId",
                table: "GradientModelRun",
                column: "RunsId");

            migrationBuilder.CreateIndex(
                name: "IX_GradientModels_InjectionWellId",
                table: "GradientModels",
                column: "InjectionWellId");

            migrationBuilder.CreateIndex(
                name: "IX_GradientModels_TargetWellId",
                table: "GradientModels",
                column: "TargetWellId");

            migrationBuilder.CreateIndex(
                name: "IX_RotaryModelRun_RunsId",
                table: "RotaryModelRun",
                column: "RunsId");

            migrationBuilder.CreateIndex(
                name: "IX_RotaryModels_InjectionWellId",
                table: "RotaryModels",
                column: "InjectionWellId");

            migrationBuilder.CreateIndex(
                name: "IX_RotaryModels_TargetWellId",
                table: "RotaryModels",
                column: "TargetWellId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedGradientModels_CreationTime",
                table: "SavedGradientModels",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_SavedGradientModels_GradientModelId",
                table: "SavedGradientModels",
                column: "GradientModelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradientModelRun");

            migrationBuilder.DropTable(
                name: "RotaryModelRun");

            migrationBuilder.DropTable(
                name: "SavedGradientModels");

            migrationBuilder.DropTable(
                name: "RotaryModels");

            migrationBuilder.DropTable(
                name: "GradientModels");
        }
    }
}

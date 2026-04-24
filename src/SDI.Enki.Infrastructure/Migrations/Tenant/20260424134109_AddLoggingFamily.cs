using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddLoggingFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoggingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrackDepth = table.Column<bool>(type: "bit", nullable: false),
                    DepthOutputInterval = table.Column<double>(type: "float", nullable: false),
                    ProcessOneDirection = table.Column<bool>(type: "bit", nullable: false),
                    AverageOverInterval = table.Column<bool>(type: "bit", nullable: false),
                    ShowQualifyPlots = table.Column<bool>(type: "bit", nullable: false),
                    ReverseGsSign = table.Column<bool>(type: "bit", nullable: false),
                    DepthOffset = table.Column<double>(type: "float", nullable: false),
                    OutputEfd = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoggingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Loggings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CalibrationId = table.Column<int>(type: "int", nullable: false),
                    MagneticId = table.Column<int>(type: "int", nullable: false),
                    LogSettingId = table.Column<int>(type: "int", nullable: false),
                    GradientRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RotaryRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PassiveRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loggings", x => x.Id);
                    table.CheckConstraint("CK_Loggings_ExactlyOneRun", "(CASE WHEN [GradientRunId] IS NULL THEN 0 ELSE 1 END) + (CASE WHEN [RotaryRunId]   IS NULL THEN 0 ELSE 1 END) + (CASE WHEN [PassiveRunId]  IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_Loggings_Calibrations_CalibrationId",
                        column: x => x.CalibrationId,
                        principalTable: "Calibrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loggings_LoggingSettings_LogSettingId",
                        column: x => x.LogSettingId,
                        principalTable: "LoggingSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loggings_Magnetics_MagneticId",
                        column: x => x.MagneticId,
                        principalTable: "Magnetics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loggings_Runs_GradientRunId",
                        column: x => x.GradientRunId,
                        principalTable: "Runs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Loggings_Runs_PassiveRunId",
                        column: x => x.PassiveRunId,
                        principalTable: "Runs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Loggings_Runs_RotaryRunId",
                        column: x => x.RotaryRunId,
                        principalTable: "Runs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LoggingEfd",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoggingId = table.Column<int>(type: "int", nullable: false),
                    MeasuredDepth = table.Column<double>(type: "float", nullable: false),
                    Bx = table.Column<double>(type: "float", nullable: false),
                    By = table.Column<double>(type: "float", nullable: false),
                    Bz = table.Column<double>(type: "float", nullable: false),
                    Gx = table.Column<double>(type: "float", nullable: false),
                    Gy = table.Column<double>(type: "float", nullable: false),
                    Gz = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoggingEfd", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoggingEfd_Loggings_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Loggings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoggingFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoggingId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    File = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoggingFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoggingFiles_Loggings_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Loggings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoggingProcessing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoggingId = table.Column<int>(type: "int", nullable: false),
                    IsLodestone = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoggingProcessing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoggingProcessing_Loggings_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Loggings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoggingTimeDepth",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoggingId = table.Column<int>(type: "int", nullable: false),
                    ShotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TimeInterval = table.Column<double>(type: "float", nullable: false),
                    StartTime = table.Column<double>(type: "float", nullable: false),
                    EndTime = table.Column<double>(type: "float", nullable: false),
                    StartDepth = table.Column<double>(type: "float", nullable: false),
                    EndDepth = table.Column<double>(type: "float", nullable: false),
                    Closed = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoggingTimeDepth", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoggingTimeDepth_Loggings_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Loggings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoggingId = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<double>(type: "float", nullable: false),
                    Bx = table.Column<double>(type: "float", nullable: false),
                    By = table.Column<double>(type: "float", nullable: false),
                    Bz = table.Column<double>(type: "float", nullable: false),
                    Gx = table.Column<double>(type: "float", nullable: false),
                    Gy = table.Column<double>(type: "float", nullable: false),
                    Gz = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Logs_Loggings_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Loggings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PassiveLoggingProcessing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoggingId = table.Column<int>(type: "int", nullable: false),
                    IsLodestone = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PassiveLoggingProcessing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PassiveLoggingProcessing_Loggings_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Loggings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotaryProcessing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoggingId = table.Column<int>(type: "int", nullable: false),
                    IsLodestone = table.Column<bool>(type: "bit", nullable: false),
                    Units = table.Column<bool>(type: "bit", nullable: false),
                    LowCutoff = table.Column<double>(type: "float", nullable: false),
                    HighCutoff = table.Column<double>(type: "float", nullable: false),
                    Current = table.Column<double>(type: "float", nullable: false),
                    SurveyMagUsed = table.Column<int>(type: "int", nullable: false),
                    AutoResend = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotaryProcessing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotaryProcessing_Loggings_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Loggings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LogTimeDepth",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoggingTimeDepthId = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<double>(type: "float", nullable: false),
                    Depth = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogTimeDepth", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogTimeDepth_LoggingTimeDepth_LoggingTimeDepthId",
                        column: x => x.LoggingTimeDepthId,
                        principalTable: "LoggingTimeDepth",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoggingEfd_LoggingId",
                table: "LoggingEfd",
                column: "LoggingId");

            migrationBuilder.CreateIndex(
                name: "IX_LoggingFiles_LoggingId",
                table: "LoggingFiles",
                column: "LoggingId");

            migrationBuilder.CreateIndex(
                name: "IX_LoggingProcessing_LoggingId",
                table: "LoggingProcessing",
                column: "LoggingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loggings_CalibrationId",
                table: "Loggings",
                column: "CalibrationId");

            migrationBuilder.CreateIndex(
                name: "IX_Loggings_GradientRunId",
                table: "Loggings",
                column: "GradientRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Loggings_LogSettingId",
                table: "Loggings",
                column: "LogSettingId");

            migrationBuilder.CreateIndex(
                name: "IX_Loggings_MagneticId",
                table: "Loggings",
                column: "MagneticId");

            migrationBuilder.CreateIndex(
                name: "IX_Loggings_PassiveRunId",
                table: "Loggings",
                column: "PassiveRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Loggings_RotaryRunId",
                table: "Loggings",
                column: "RotaryRunId");

            migrationBuilder.CreateIndex(
                name: "IX_LoggingTimeDepth_LoggingId",
                table: "LoggingTimeDepth",
                column: "LoggingId");

            migrationBuilder.CreateIndex(
                name: "IX_Logs_LoggingId",
                table: "Logs",
                column: "LoggingId");

            migrationBuilder.CreateIndex(
                name: "IX_Logs_LoggingId_Depth",
                table: "Logs",
                columns: new[] { "LoggingId", "Depth" });

            migrationBuilder.CreateIndex(
                name: "IX_LogTimeDepth_LoggingTimeDepthId",
                table: "LogTimeDepth",
                column: "LoggingTimeDepthId");

            migrationBuilder.CreateIndex(
                name: "IX_PassiveLoggingProcessing_LoggingId",
                table: "PassiveLoggingProcessing",
                column: "LoggingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RotaryProcessing_LoggingId",
                table: "RotaryProcessing",
                column: "LoggingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoggingEfd");

            migrationBuilder.DropTable(
                name: "LoggingFiles");

            migrationBuilder.DropTable(
                name: "LoggingProcessing");

            migrationBuilder.DropTable(
                name: "Logs");

            migrationBuilder.DropTable(
                name: "LogTimeDepth");

            migrationBuilder.DropTable(
                name: "PassiveLoggingProcessing");

            migrationBuilder.DropTable(
                name: "RotaryProcessing");

            migrationBuilder.DropTable(
                name: "LoggingTimeDepth");

            migrationBuilder.DropTable(
                name: "Loggings");

            migrationBuilder.DropTable(
                name: "LoggingSettings");
        }
    }
}

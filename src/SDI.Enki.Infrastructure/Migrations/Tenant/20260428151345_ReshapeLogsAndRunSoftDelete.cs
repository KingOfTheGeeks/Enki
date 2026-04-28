using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <summary>
    /// Phase-1 Run/Log reshape:
    ///
    /// <list type="number">
    ///   <item>Drops the legacy <c>Logging</c> family of tables
    ///   (<c>Logging</c> + 9 satellites). Pre-customer; no tenant DB
    ///   carries Logging data — the entity was never seeded, no
    ///   controllers populated it. Drop-and-recreate is the right
    ///   shape; data preservation isn't a concern.</item>
    ///
    ///   <item>Adds <c>Run.ArchivedAt</c> + index for the soft-delete
    ///   pattern that's already on <c>Well</c>.</item>
    ///
    ///   <item>Creates the new <c>Log</c> family — parent
    ///   <c>Log</c> (now <see cref="SDI.Enki.Core.Abstractions.IAuditable"/>;
    ///   single <c>RunId</c> FK rather than the legacy three nullable
    ///   run-FKs + CHECK constraint), plus eight satellite tables
    ///   (LogSample, LogFile, LogTimeWindow, LogTimeWindowSample,
    ///   LogEfdSample, LogProcessing, RotaryLogProcessing,
    ///   PassiveLogProcessing) and the LogSetting lookup.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Down() is intentionally not symmetric.</b> The legacy shape
    /// it would restore was the source-of-bugs pattern the reshape
    /// fixes — no scenario where rolling back into it is desirable.
    /// If you need to step backward through history, do it via
    /// <c>git checkout</c> of the prior migration tree + recreate
    /// the tenant DBs (we have no production tenants to preserve).
    /// </para>
    /// </summary>
    public partial class ReshapeLogsAndRunSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- 1. Drop legacy Logging family ----------
            // Order matters — children before parents.

            // Drop the FK that points at Logging from the old Log
            // sample table first so the parent drop succeeds.
            migrationBuilder.DropForeignKey(
                name: "FK_Log_Logging_LoggingId",
                table: "Log");

            migrationBuilder.DropTable(name: "LoggingProcessing");
            migrationBuilder.DropTable(name: "RotaryProcessing");
            migrationBuilder.DropTable(name: "PassiveLoggingProcessing");
            migrationBuilder.DropTable(name: "LoggingEfd");
            migrationBuilder.DropTable(name: "LogTimeDepth");      // sample under LoggingTimeDepth
            migrationBuilder.DropTable(name: "LoggingTimeDepth");
            migrationBuilder.DropTable(name: "LoggingFile");
            migrationBuilder.DropTable(name: "Log");               // old sample table — same name will be reused for the new parent below
            migrationBuilder.DropTable(name: "Logging");
            migrationBuilder.DropTable(name: "LoggingSetting");

            // ---------- 2. Run gains ArchivedAt (soft-delete) ----------
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "Run",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Run_ArchivedAt",
                table: "Run",
                column: "ArchivedAt");

            // ---------- 3. Create new Log family ----------

            // LogSetting — no FKs. Lookup-only.
            migrationBuilder.CreateTable(
                name: "LogSetting",
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
                    OutputEfd = table.Column<bool>(type: "bit", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogSetting", x => x.Id);
                });

            // Log — parent. IAuditable + RowVersion + single RunId FK.
            migrationBuilder.CreateTable(
                name: "Log",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    // Lookup FKs are nullable — see Log.cs for the MVP rationale.
                    CalibrationId = table.Column<int>(type: "int", nullable: true),
                    MagneticId = table.Column<int>(type: "int", nullable: true),
                    LogSettingId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Log", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Log_Calibration_CalibrationId",
                        column: x => x.CalibrationId,
                        principalTable: "Calibration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Log_Magnetics_MagneticId",
                        column: x => x.MagneticId,
                        principalTable: "Magnetics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Log_LogSetting_LogSettingId",
                        column: x => x.LogSettingId,
                        principalTable: "LogSetting",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Log_Run_RunId",
                        column: x => x.RunId,
                        principalTable: "Run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Log_RunId",
                table: "Log",
                column: "RunId");
            migrationBuilder.CreateIndex(
                name: "IX_Log_CalibrationId",
                table: "Log",
                column: "CalibrationId");
            migrationBuilder.CreateIndex(
                name: "IX_Log_MagneticId",
                table: "Log",
                column: "MagneticId");
            migrationBuilder.CreateIndex(
                name: "IX_Log_LogSettingId",
                table: "Log",
                column: "LogSettingId");

            // LogFile — binary attachments; CASCADE on parent delete.
            migrationBuilder.CreateTable(
                name: "LogFile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    File = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogFile_Log_LogId",
                        column: x => x.LogId,
                        principalTable: "Log",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_LogFile_LogId",
                table: "LogFile",
                column: "LogId");

            // LogSample — per-depth Bx/By/Bz/Gx/Gy/Gz triplets.
            migrationBuilder.CreateTable(
                name: "LogSample",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogId = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<double>(type: "float", nullable: false),
                    Bx = table.Column<double>(type: "float", nullable: false),
                    By = table.Column<double>(type: "float", nullable: false),
                    Bz = table.Column<double>(type: "float", nullable: false),
                    Gx = table.Column<double>(type: "float", nullable: false),
                    Gy = table.Column<double>(type: "float", nullable: false),
                    Gz = table.Column<double>(type: "float", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogSample", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogSample_Log_LogId",
                        column: x => x.LogId,
                        principalTable: "Log",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_LogSample_LogId",
                table: "LogSample",
                column: "LogId");
            migrationBuilder.CreateIndex(
                name: "IX_LogSample_LogId_Depth",
                table: "LogSample",
                columns: new[] { "LogId", "Depth" });

            // LogTimeWindow — header for time→depth samples.
            migrationBuilder.CreateTable(
                name: "LogTimeWindow",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogId = table.Column<int>(type: "int", nullable: false),
                    ShotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TimeInterval = table.Column<double>(type: "float", nullable: false),
                    StartTime = table.Column<double>(type: "float", nullable: false),
                    EndTime = table.Column<double>(type: "float", nullable: false),
                    StartDepth = table.Column<double>(type: "float", nullable: false),
                    EndDepth = table.Column<double>(type: "float", nullable: false),
                    Closed = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogTimeWindow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogTimeWindow_Log_LogId",
                        column: x => x.LogId,
                        principalTable: "Log",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_LogTimeWindow_LogId",
                table: "LogTimeWindow",
                column: "LogId");

            // LogTimeWindowSample — (time, depth) pairs inside a window.
            migrationBuilder.CreateTable(
                name: "LogTimeWindowSample",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogTimeWindowId = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<double>(type: "float", nullable: false),
                    Depth = table.Column<double>(type: "float", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogTimeWindowSample", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogTimeWindowSample_LogTimeWindow_LogTimeWindowId",
                        column: x => x.LogTimeWindowId,
                        principalTable: "LogTimeWindow",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_LogTimeWindowSample_LogTimeWindowId",
                table: "LogTimeWindowSample",
                column: "LogTimeWindowId");

            // LogEfdSample — EFD samples.
            migrationBuilder.CreateTable(
                name: "LogEfdSample",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogId = table.Column<int>(type: "int", nullable: false),
                    MeasuredDepth = table.Column<double>(type: "float", nullable: false),
                    Bx = table.Column<double>(type: "float", nullable: false),
                    By = table.Column<double>(type: "float", nullable: false),
                    Bz = table.Column<double>(type: "float", nullable: false),
                    Gx = table.Column<double>(type: "float", nullable: false),
                    Gy = table.Column<double>(type: "float", nullable: false),
                    Gz = table.Column<double>(type: "float", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogEfdSample", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogEfdSample_Log_LogId",
                        column: x => x.LogId,
                        principalTable: "Log",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_LogEfdSample_LogId",
                table: "LogEfdSample",
                column: "LogId");

            // LogProcessing — Gradient processing (1:0..1 with Log).
            migrationBuilder.CreateTable(
                name: "LogProcessing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogId = table.Column<int>(type: "int", nullable: false),
                    IsLodestone = table.Column<bool>(type: "bit", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogProcessing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogProcessing_Log_LogId",
                        column: x => x.LogId,
                        principalTable: "Log",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_LogProcessing_LogId",
                table: "LogProcessing",
                column: "LogId",
                unique: true);

            // RotaryLogProcessing — Rotary processing (1:0..1 with Log; carries extra columns).
            migrationBuilder.CreateTable(
                name: "RotaryLogProcessing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogId = table.Column<int>(type: "int", nullable: false),
                    IsLodestone = table.Column<bool>(type: "bit", nullable: false),
                    Units = table.Column<bool>(type: "bit", nullable: false),
                    LowCutoff = table.Column<double>(type: "float", nullable: false),
                    HighCutoff = table.Column<double>(type: "float", nullable: false),
                    Current = table.Column<double>(type: "float", nullable: false),
                    SurveyMagUsed = table.Column<int>(type: "int", nullable: false),
                    AutoResend = table.Column<bool>(type: "bit", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotaryLogProcessing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotaryLogProcessing_Log_LogId",
                        column: x => x.LogId,
                        principalTable: "Log",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_RotaryLogProcessing_LogId",
                table: "RotaryLogProcessing",
                column: "LogId",
                unique: true);

            // PassiveLogProcessing — Passive processing (1:0..1 with Log).
            migrationBuilder.CreateTable(
                name: "PassiveLogProcessing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogId = table.Column<int>(type: "int", nullable: false),
                    IsLodestone = table.Column<bool>(type: "bit", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PassiveLogProcessing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PassiveLogProcessing_Log_LogId",
                        column: x => x.LogId,
                        principalTable: "Log",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_PassiveLogProcessing_LogId",
                table: "PassiveLogProcessing",
                column: "LogId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The legacy three-FK Logging shape this migration replaced
            // was the source of the bugs the reshape fixes — there's no
            // scenario where rolling back into it is desirable. We have
            // no production tenants; if you need to step backward
            // through history, use `git checkout` of the prior tree
            // and recreate the tenant DBs.
            throw new NotSupportedException(
                "Down migration is not supported for ReshapeLogsAndRunSoftDelete. " +
                "This migration restructures the Log family in a one-way fashion. " +
                "To roll back, check out the prior commit and recreate tenant DBs.");
        }
    }
}

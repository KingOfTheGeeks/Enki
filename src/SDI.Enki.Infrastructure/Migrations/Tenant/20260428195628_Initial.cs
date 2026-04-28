using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedColumns = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Calibration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CalibrationString = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calibration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Job",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WellName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UnitSystem = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LogoName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Job", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Operator",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operator", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobUser",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobUser", x => new { x.JobId, x.UserId });
                    table.ForeignKey(
                        name: "FK_JobUser_Job_JobId",
                        column: x => x.JobId,
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReferencedJob",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferencedTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferencedJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferencedJob", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferencedJob_Job_JobId",
                        column: x => x.JobId,
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Run",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartDepth = table.Column<double>(type: "float", nullable: false),
                    EndDepth = table.Column<double>(type: "float", nullable: false),
                    StartTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BridleLength = table.Column<double>(type: "float", nullable: true),
                    CurrentInjection = table.Column<double>(type: "float", nullable: true),
                    PassiveBinary = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    PassiveBinaryName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PassiveBinaryUploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PassiveConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PassiveConfigUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PassiveResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PassiveResultComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PassiveResultMardukVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PassiveResultStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PassiveResultError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Run", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Run_Job_JobId",
                        column: x => x.JobId,
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Well",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Well", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Well_Job_JobId",
                        column: x => x.JobId,
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Log",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CalibrationId = table.Column<int>(type: "int", nullable: true),
                    Binary = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    BinaryName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    BinaryUploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfigUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
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
                        name: "FK_Log_Run_RunId",
                        column: x => x.RunId,
                        principalTable: "Run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                        name: "FK_RunOperator_Operator_OperatorsId",
                        column: x => x.OperatorsId,
                        principalTable: "Operator",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RunOperator_Run_RunsId",
                        column: x => x.RunsId,
                        principalTable: "Run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Shot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CalibrationId = table.Column<int>(type: "int", nullable: true),
                    Binary = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    BinaryName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    BinaryUploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfigUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResultMardukVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ResultStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ResultError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GyroBinary = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    GyroBinaryName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    GyroBinaryUploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GyroConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GyroConfigUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GyroResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GyroResultComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GyroResultMardukVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GyroResultStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    GyroResultError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shot_Calibration_CalibrationId",
                        column: x => x.CalibrationId,
                        principalTable: "Calibration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shot_Run_RunId",
                        column: x => x.RunId,
                        principalTable: "Run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommonMeasure",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WellId = table.Column<int>(type: "int", nullable: false),
                    FromVertical = table.Column<double>(type: "float", nullable: false),
                    ToVertical = table.Column<double>(type: "float", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonMeasure", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonMeasure_Well_WellId",
                        column: x => x.WellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Formation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WellId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FromVertical = table.Column<double>(type: "float", nullable: false),
                    ToVertical = table.Column<double>(type: "float", nullable: false),
                    Resistance = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Formation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Formation_Well_WellId",
                        column: x => x.WellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradientModel",
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
                    table.PrimaryKey("PK_GradientModel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradientModel_Well_InjectionWellId",
                        column: x => x.InjectionWellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradientModel_Well_TargetWellId",
                        column: x => x.TargetWellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Magnetics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WellId = table.Column<int>(type: "int", nullable: true),
                    BTotal = table.Column<double>(type: "float", nullable: false),
                    Dip = table.Column<double>(type: "float", nullable: false),
                    Declination = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Magnetics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Magnetics_Well_WellId",
                        column: x => x.WellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotaryModel",
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
                    table.PrimaryKey("PK_RotaryModel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotaryModel_Well_InjectionWellId",
                        column: x => x.InjectionWellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RotaryModel_Well_TargetWellId",
                        column: x => x.TargetWellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Survey",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WellId = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<double>(type: "float", nullable: false),
                    Inclination = table.Column<double>(type: "float", nullable: false),
                    Azimuth = table.Column<double>(type: "float", nullable: false),
                    VerticalDepth = table.Column<double>(type: "float", nullable: false),
                    SubSea = table.Column<double>(type: "float", nullable: false),
                    North = table.Column<double>(type: "float", nullable: false),
                    East = table.Column<double>(type: "float", nullable: false),
                    DoglegSeverity = table.Column<double>(type: "float", nullable: false),
                    VerticalSection = table.Column<double>(type: "float", nullable: false),
                    Northing = table.Column<double>(type: "float", nullable: false),
                    Easting = table.Column<double>(type: "float", nullable: false),
                    Build = table.Column<double>(type: "float", nullable: false),
                    Turn = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Survey", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Survey_Well_WellId",
                        column: x => x.WellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TieOn",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WellId = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<double>(type: "float", nullable: false),
                    Inclination = table.Column<double>(type: "float", nullable: false),
                    Azimuth = table.Column<double>(type: "float", nullable: false),
                    North = table.Column<double>(type: "float", nullable: false),
                    East = table.Column<double>(type: "float", nullable: false),
                    Northing = table.Column<double>(type: "float", nullable: false),
                    Easting = table.Column<double>(type: "float", nullable: false),
                    VerticalReference = table.Column<double>(type: "float", nullable: false),
                    SubSeaReference = table.Column<double>(type: "float", nullable: false),
                    VerticalSectionDirection = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TieOn", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TieOn_Well_WellId",
                        column: x => x.WellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tubular",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WellId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FromMeasured = table.Column<double>(type: "float", nullable: false),
                    ToMeasured = table.Column<double>(type: "float", nullable: false),
                    Diameter = table.Column<double>(type: "float", nullable: false),
                    Weight = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tubular", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tubular_Well_WellId",
                        column: x => x.WellId,
                        principalTable: "Well",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LogResultFile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogResultFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogResultFile_Log_LogId",
                        column: x => x.LogId,
                        principalTable: "Log",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShotId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    User = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Identity = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comment_Shot_ShotId",
                        column: x => x.ShotId,
                        principalTable: "Shot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                        name: "FK_GradientModelRun_GradientModel_GradientModelsId",
                        column: x => x.GradientModelsId,
                        principalTable: "GradientModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GradientModelRun_Run_RunsId",
                        column: x => x.RunsId,
                        principalTable: "Run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedGradientModel",
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
                    table.PrimaryKey("PK_SavedGradientModel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedGradientModel_GradientModel_GradientModelId",
                        column: x => x.GradientModelId,
                        principalTable: "GradientModel",
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
                        name: "FK_RotaryModelRun_RotaryModel_RotaryModelsId",
                        column: x => x.RotaryModelsId,
                        principalTable: "RotaryModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RotaryModelRun_Run_RunsId",
                        column: x => x.RunsId,
                        principalTable: "Run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_ChangedAt",
                table: "AuditLog",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EntityType_EntityId",
                table: "AuditLog",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Calibration_Name_CalibrationString",
                table: "Calibration",
                columns: new[] { "Name", "CalibrationString" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Comment_ShotId",
                table: "Comment",
                column: "ShotId");

            migrationBuilder.CreateIndex(
                name: "IX_CommonMeasure_WellId",
                table: "CommonMeasure",
                column: "WellId");

            migrationBuilder.CreateIndex(
                name: "IX_Formation_WellId",
                table: "Formation",
                column: "WellId");

            migrationBuilder.CreateIndex(
                name: "IX_GradientModel_InjectionWellId",
                table: "GradientModel",
                column: "InjectionWellId");

            migrationBuilder.CreateIndex(
                name: "IX_GradientModel_TargetWellId",
                table: "GradientModel",
                column: "TargetWellId");

            migrationBuilder.CreateIndex(
                name: "IX_GradientModelRun_RunsId",
                table: "GradientModelRun",
                column: "RunsId");

            migrationBuilder.CreateIndex(
                name: "IX_Job_Region",
                table: "Job",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_Job_Status",
                table: "Job",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_JobUser_UserId",
                table: "JobUser",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Log_CalibrationId",
                table: "Log",
                column: "CalibrationId");

            migrationBuilder.CreateIndex(
                name: "IX_Log_RunId",
                table: "Log",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_LogResultFile_LogId",
                table: "LogResultFile",
                column: "LogId");

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

            migrationBuilder.CreateIndex(
                name: "IX_Operator_Name",
                table: "Operator",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ReferencedJob_JobId",
                table: "ReferencedJob",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferencedJob_ReferencedTenantId_ReferencedJobId",
                table: "ReferencedJob",
                columns: new[] { "ReferencedTenantId", "ReferencedJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_RotaryModel_InjectionWellId",
                table: "RotaryModel",
                column: "InjectionWellId");

            migrationBuilder.CreateIndex(
                name: "IX_RotaryModel_TargetWellId",
                table: "RotaryModel",
                column: "TargetWellId");

            migrationBuilder.CreateIndex(
                name: "IX_RotaryModelRun_RunsId",
                table: "RotaryModelRun",
                column: "RunsId");

            migrationBuilder.CreateIndex(
                name: "IX_Run_ArchivedAt",
                table: "Run",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Run_JobId",
                table: "Run",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_Run_PassiveResultStatus",
                table: "Run",
                column: "PassiveResultStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Run_Type",
                table: "Run",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_RunOperator_RunsId",
                table: "RunOperator",
                column: "RunsId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedGradientModel_CreationTime",
                table: "SavedGradientModel",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_SavedGradientModel_GradientModelId",
                table: "SavedGradientModel",
                column: "GradientModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Shot_CalibrationId",
                table: "Shot",
                column: "CalibrationId");

            migrationBuilder.CreateIndex(
                name: "IX_Shot_GyroResultStatus",
                table: "Shot",
                column: "GyroResultStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Shot_ResultStatus",
                table: "Shot",
                column: "ResultStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Shot_RunId",
                table: "Shot",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Survey_WellId",
                table: "Survey",
                column: "WellId");

            migrationBuilder.CreateIndex(
                name: "IX_Survey_WellId_Depth",
                table: "Survey",
                columns: new[] { "WellId", "Depth" });

            migrationBuilder.CreateIndex(
                name: "IX_TieOn_WellId",
                table: "TieOn",
                column: "WellId");

            migrationBuilder.CreateIndex(
                name: "IX_Tubular_WellId",
                table: "Tubular",
                column: "WellId");

            migrationBuilder.CreateIndex(
                name: "IX_Tubular_WellId_Order",
                table: "Tubular",
                columns: new[] { "WellId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Well_ArchivedAt",
                table: "Well",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Well_JobId",
                table: "Well",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "Comment");

            migrationBuilder.DropTable(
                name: "CommonMeasure");

            migrationBuilder.DropTable(
                name: "Formation");

            migrationBuilder.DropTable(
                name: "GradientModelRun");

            migrationBuilder.DropTable(
                name: "JobUser");

            migrationBuilder.DropTable(
                name: "LogResultFile");

            migrationBuilder.DropTable(
                name: "Magnetics");

            migrationBuilder.DropTable(
                name: "ReferencedJob");

            migrationBuilder.DropTable(
                name: "RotaryModelRun");

            migrationBuilder.DropTable(
                name: "RunOperator");

            migrationBuilder.DropTable(
                name: "SavedGradientModel");

            migrationBuilder.DropTable(
                name: "Survey");

            migrationBuilder.DropTable(
                name: "TieOn");

            migrationBuilder.DropTable(
                name: "Tubular");

            migrationBuilder.DropTable(
                name: "Shot");

            migrationBuilder.DropTable(
                name: "Log");

            migrationBuilder.DropTable(
                name: "RotaryModel");

            migrationBuilder.DropTable(
                name: "Operator");

            migrationBuilder.DropTable(
                name: "GradientModel");

            migrationBuilder.DropTable(
                name: "Calibration");

            migrationBuilder.DropTable(
                name: "Run");

            migrationBuilder.DropTable(
                name: "Well");

            migrationBuilder.DropTable(
                name: "Job");
        }
    }
}

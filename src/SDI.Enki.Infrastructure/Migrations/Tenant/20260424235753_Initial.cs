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
                name: "Comment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    User = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Identity = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comment", x => x.Id);
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
                name: "LoggingSetting",
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
                    table.PrimaryKey("PK_LoggingSetting", x => x.Id);
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
                name: "Well",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Well", x => x.Id);
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
                    BridleLength = table.Column<double>(type: "float", nullable: true),
                    CurrentInjection = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
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
                name: "CommonMeasure",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WellId = table.Column<int>(type: "int", nullable: false),
                    FromVertical = table.Column<double>(type: "float", nullable: false),
                    ToVertical = table.Column<double>(type: "float", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false)
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
                    Resistance = table.Column<double>(type: "float", nullable: false)
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
                    Turn = table.Column<double>(type: "float", nullable: false)
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
                    VerticalSectionDirection = table.Column<double>(type: "float", nullable: false)
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
                    Weight = table.Column<double>(type: "float", nullable: false)
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
                name: "Gradient",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Voltage = table.Column<double>(type: "float", nullable: true),
                    Frequency = table.Column<double>(type: "float", nullable: true),
                    Frame = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gradient", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Gradient_Gradient_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Gradient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Gradient_Run_RunId",
                        column: x => x.RunId,
                        principalTable: "Run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Logging",
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
                    table.PrimaryKey("PK_Logging", x => x.Id);
                    table.CheckConstraint("CK_Loggings_ExactlyOneRun", "(CASE WHEN [GradientRunId] IS NULL THEN 0 ELSE 1 END) + (CASE WHEN [RotaryRunId]   IS NULL THEN 0 ELSE 1 END) + (CASE WHEN [PassiveRunId]  IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_Logging_Calibration_CalibrationId",
                        column: x => x.CalibrationId,
                        principalTable: "Calibration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Logging_LoggingSetting_LogSettingId",
                        column: x => x.LogSettingId,
                        principalTable: "LoggingSetting",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Logging_Magnetics_MagneticId",
                        column: x => x.MagneticId,
                        principalTable: "Magnetics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Logging_Run_GradientRunId",
                        column: x => x.GradientRunId,
                        principalTable: "Run",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Logging_Run_PassiveRunId",
                        column: x => x.PassiveRunId,
                        principalTable: "Run",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Logging_Run_RotaryRunId",
                        column: x => x.RotaryRunId,
                        principalTable: "Run",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Passive",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AziToTarget = table.Column<double>(type: "float", nullable: false),
                    Azimuth = table.Column<double>(type: "float", nullable: false),
                    Inclination = table.Column<double>(type: "float", nullable: false),
                    MdToTarget = table.Column<double>(type: "float", nullable: false),
                    MeasuredDepth = table.Column<double>(type: "float", nullable: false),
                    TfToTarget = table.Column<double>(type: "float", nullable: false),
                    Toolface = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Passive", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Passive_Run_RunId",
                        column: x => x.RunId,
                        principalTable: "Run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rotary",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Frame = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rotary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rotary_Rotary_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Rotary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rotary_Run_RunId",
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

            migrationBuilder.CreateTable(
                name: "GradientComment",
                columns: table => new
                {
                    CommentsId = table.Column<int>(type: "int", nullable: false),
                    GradientsId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradientComment", x => new { x.CommentsId, x.GradientsId });
                    table.ForeignKey(
                        name: "FK_GradientComment_Comment_CommentsId",
                        column: x => x.CommentsId,
                        principalTable: "Comment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GradientComment_Gradient_GradientsId",
                        column: x => x.GradientsId,
                        principalTable: "Gradient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradientFile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GradientId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    File = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradientFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradientFile_Gradient_GradientId",
                        column: x => x.GradientId,
                        principalTable: "Gradient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradientSolution",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GradientId = table.Column<int>(type: "int", nullable: false),
                    MeasuredDepth = table.Column<double>(type: "float", nullable: false),
                    Inclination = table.Column<double>(type: "float", nullable: false),
                    Azimuth = table.Column<double>(type: "float", nullable: false),
                    Toolface = table.Column<double>(type: "float", nullable: false),
                    MdToTarget = table.Column<double>(type: "float", nullable: false),
                    AziToTarget = table.Column<double>(type: "float", nullable: false),
                    TfToTarget = table.Column<double>(type: "float", nullable: false),
                    SignFlipped = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradientSolution", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradientSolution_Gradient_GradientId",
                        column: x => x.GradientId,
                        principalTable: "Gradient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Log",
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
                    table.PrimaryKey("PK_Log", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Log_Logging_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Logging",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                        name: "FK_LoggingEfd_Logging_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Logging",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoggingFile",
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
                    table.PrimaryKey("PK_LoggingFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoggingFile_Logging_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Logging",
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
                        name: "FK_LoggingProcessing_Logging_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Logging",
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
                        name: "FK_LoggingTimeDepth_Logging_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Logging",
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
                        name: "FK_PassiveLoggingProcessing_Logging_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Logging",
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
                        name: "FK_RotaryProcessing_Logging_LoggingId",
                        column: x => x.LoggingId,
                        principalTable: "Logging",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PassiveComment",
                columns: table => new
                {
                    CommentsId = table.Column<int>(type: "int", nullable: false),
                    PassivesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PassiveComment", x => new { x.CommentsId, x.PassivesId });
                    table.ForeignKey(
                        name: "FK_PassiveComment_Comment_CommentsId",
                        column: x => x.CommentsId,
                        principalTable: "Comment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PassiveComment_Passive_PassivesId",
                        column: x => x.PassivesId,
                        principalTable: "Passive",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PassiveFile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PassiveId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    File = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PassiveFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PassiveFile_Passive_PassiveId",
                        column: x => x.PassiveId,
                        principalTable: "Passive",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotaryComment",
                columns: table => new
                {
                    CommentsId = table.Column<int>(type: "int", nullable: false),
                    RotariesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotaryComment", x => new { x.CommentsId, x.RotariesId });
                    table.ForeignKey(
                        name: "FK_RotaryComment_Comment_CommentsId",
                        column: x => x.CommentsId,
                        principalTable: "Comment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RotaryComment_Rotary_RotariesId",
                        column: x => x.RotariesId,
                        principalTable: "Rotary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotaryFile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RotaryId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    File = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotaryFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotaryFile_Rotary_RotaryId",
                        column: x => x.RotaryId,
                        principalTable: "Rotary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotarySolution",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RotaryId = table.Column<int>(type: "int", nullable: false),
                    RotorMeasuredDepth = table.Column<double>(type: "float", nullable: false),
                    RotorInclination = table.Column<double>(type: "float", nullable: false),
                    RotorAzimuth = table.Column<double>(type: "float", nullable: false),
                    RotorMoment = table.Column<int>(type: "int", nullable: false),
                    PassByTotalDistance = table.Column<double>(type: "float", nullable: false),
                    PassByApproachAngle = table.Column<double>(type: "float", nullable: false),
                    DistanceToPassBy = table.Column<double>(type: "float", nullable: false),
                    DistanceAtPassBy = table.Column<double>(type: "float", nullable: false),
                    NorthToSensor = table.Column<double>(type: "float", nullable: false),
                    EastToSensor = table.Column<double>(type: "float", nullable: false),
                    VerticalToSensor = table.Column<double>(type: "float", nullable: false),
                    HighSideToSensor = table.Column<double>(type: "float", nullable: false),
                    RightSideToSensor = table.Column<double>(type: "float", nullable: false),
                    AxialToSensor = table.Column<double>(type: "float", nullable: false),
                    SensorMeasuredDepth = table.Column<double>(type: "float", nullable: false),
                    SensorInclination = table.Column<double>(type: "float", nullable: false),
                    SensorAzimuth = table.Column<double>(type: "float", nullable: false),
                    SensorToolface = table.Column<double>(type: "float", nullable: false),
                    SensorShieldMatrixXY = table.Column<double>(type: "float", nullable: false),
                    SensorShieldMatrixZ = table.Column<double>(type: "float", nullable: false),
                    SensorLobe = table.Column<int>(type: "int", nullable: false),
                    SensorMagnetometer = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotarySolution", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotarySolution_Rotary_RotaryId",
                        column: x => x.RotaryId,
                        principalTable: "Rotary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Shot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToolUptime = table.Column<int>(type: "int", nullable: false),
                    ShotTime = table.Column<int>(type: "int", nullable: false),
                    TimeStart = table.Column<int>(type: "int", nullable: false),
                    TimeEnd = table.Column<int>(type: "int", nullable: false),
                    NumberOfMags = table.Column<int>(type: "int", nullable: false),
                    MagneticsId = table.Column<int>(type: "int", nullable: true),
                    CalibrationsId = table.Column<int>(type: "int", nullable: true),
                    Frequency = table.Column<double>(type: "float", nullable: false),
                    Bandwidth = table.Column<double>(type: "float", nullable: false),
                    SampleFrequency = table.Column<int>(type: "int", nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: true),
                    GradientId = table.Column<int>(type: "int", nullable: true),
                    RotaryId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shot", x => x.Id);
                    table.CheckConstraint("CK_Shots_ExactlyOneParent", "([GradientId] IS NULL AND [RotaryId] IS NOT NULL) OR ([GradientId] IS NOT NULL AND [RotaryId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Shot_Calibration_CalibrationsId",
                        column: x => x.CalibrationsId,
                        principalTable: "Calibration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shot_Gradient_GradientId",
                        column: x => x.GradientId,
                        principalTable: "Gradient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shot_Magnetics_MagneticsId",
                        column: x => x.MagneticsId,
                        principalTable: "Magnetics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shot_Rotary_RotaryId",
                        column: x => x.RotaryId,
                        principalTable: "Rotary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "ActiveField",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShotId = table.Column<int>(type: "int", nullable: false),
                    Mag = table.Column<int>(type: "int", nullable: false),
                    Field = table.Column<double>(type: "float", nullable: false),
                    CosX = table.Column<double>(type: "float", nullable: false),
                    CosY = table.Column<double>(type: "float", nullable: false),
                    CosZ = table.Column<double>(type: "float", nullable: false),
                    SinX = table.Column<double>(type: "float", nullable: false),
                    SinY = table.Column<double>(type: "float", nullable: false),
                    SinZ = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveField", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActiveField_Shot_ShotId",
                        column: x => x.ShotId,
                        principalTable: "Shot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GyroShot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShotId = table.Column<int>(type: "int", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false),
                    StartTimestamp = table.Column<int>(type: "int", nullable: false),
                    Inclination = table.Column<double>(type: "float", nullable: false),
                    Azimuth = table.Column<double>(type: "float", nullable: false),
                    GyroToolface = table.Column<double>(type: "float", nullable: false),
                    HighSideToolface = table.Column<double>(type: "float", nullable: false),
                    EarthRateHorizontal = table.Column<double>(type: "float", nullable: false),
                    Temperature = table.Column<double>(type: "float", nullable: false),
                    Gain = table.Column<int>(type: "int", nullable: false),
                    Noise = table.Column<double>(type: "float", nullable: false),
                    AccelerometerQuality = table.Column<int>(type: "int", nullable: false),
                    DeltaDrift = table.Column<double>(type: "float", nullable: false),
                    DeltaBias = table.Column<double>(type: "float", nullable: false),
                    Synch = table.Column<int>(type: "int", nullable: false),
                    Index = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    ToolfaceOffset = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GyroShot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GyroShot_Shot_ShotId",
                        column: x => x.ShotId,
                        principalTable: "Shot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToolSurvey",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShotId = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<double>(type: "float", nullable: false),
                    Inclination = table.Column<double>(type: "float", nullable: false),
                    Azimuth = table.Column<double>(type: "float", nullable: false),
                    GravityToolface = table.Column<double>(type: "float", nullable: false),
                    MagneticToolface = table.Column<double>(type: "float", nullable: false),
                    Temperature = table.Column<double>(type: "float", nullable: false),
                    Current = table.Column<double>(type: "float", nullable: true),
                    Gx = table.Column<double>(type: "float", nullable: false),
                    Gy = table.Column<double>(type: "float", nullable: false),
                    Gz = table.Column<double>(type: "float", nullable: false),
                    GTotal = table.Column<double>(type: "float", nullable: false),
                    Bx = table.Column<double>(type: "float", nullable: false),
                    By = table.Column<double>(type: "float", nullable: false),
                    Bz = table.Column<double>(type: "float", nullable: false),
                    BTotal = table.Column<double>(type: "float", nullable: false),
                    Dip = table.Column<double>(type: "float", nullable: false),
                    Mag1Ab = table.Column<double>(type: "float", nullable: false),
                    Mag2Ab = table.Column<double>(type: "float", nullable: false),
                    Mag3Ab = table.Column<double>(type: "float", nullable: false),
                    Mag4Ab = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolSurvey", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolSurvey_Shot_ShotId",
                        column: x => x.ShotId,
                        principalTable: "Shot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveField_ShotId",
                table: "ActiveField",
                column: "ShotId");

            migrationBuilder.CreateIndex(
                name: "IX_Calibration_Name_CalibrationString",
                table: "Calibration",
                columns: new[] { "Name", "CalibrationString" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommonMeasure_WellId",
                table: "CommonMeasure",
                column: "WellId");

            migrationBuilder.CreateIndex(
                name: "IX_Formation_WellId",
                table: "Formation",
                column: "WellId");

            migrationBuilder.CreateIndex(
                name: "IX_Gradient_ParentId",
                table: "Gradient",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Gradient_RunId",
                table: "Gradient",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Gradient_RunId_Order",
                table: "Gradient",
                columns: new[] { "RunId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_GradientComment_GradientsId",
                table: "GradientComment",
                column: "GradientsId");

            migrationBuilder.CreateIndex(
                name: "IX_GradientFile_GradientId",
                table: "GradientFile",
                column: "GradientId");

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
                name: "IX_GradientSolution_GradientId",
                table: "GradientSolution",
                column: "GradientId");

            migrationBuilder.CreateIndex(
                name: "IX_GyroShot_ShotId",
                table: "GyroShot",
                column: "ShotId");

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
                name: "IX_Log_LoggingId",
                table: "Log",
                column: "LoggingId");

            migrationBuilder.CreateIndex(
                name: "IX_Log_LoggingId_Depth",
                table: "Log",
                columns: new[] { "LoggingId", "Depth" });

            migrationBuilder.CreateIndex(
                name: "IX_Logging_CalibrationId",
                table: "Logging",
                column: "CalibrationId");

            migrationBuilder.CreateIndex(
                name: "IX_Logging_GradientRunId",
                table: "Logging",
                column: "GradientRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Logging_LogSettingId",
                table: "Logging",
                column: "LogSettingId");

            migrationBuilder.CreateIndex(
                name: "IX_Logging_MagneticId",
                table: "Logging",
                column: "MagneticId");

            migrationBuilder.CreateIndex(
                name: "IX_Logging_PassiveRunId",
                table: "Logging",
                column: "PassiveRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Logging_RotaryRunId",
                table: "Logging",
                column: "RotaryRunId");

            migrationBuilder.CreateIndex(
                name: "IX_LoggingEfd_LoggingId",
                table: "LoggingEfd",
                column: "LoggingId");

            migrationBuilder.CreateIndex(
                name: "IX_LoggingFile_LoggingId",
                table: "LoggingFile",
                column: "LoggingId");

            migrationBuilder.CreateIndex(
                name: "IX_LoggingProcessing_LoggingId",
                table: "LoggingProcessing",
                column: "LoggingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoggingTimeDepth_LoggingId",
                table: "LoggingTimeDepth",
                column: "LoggingId");

            migrationBuilder.CreateIndex(
                name: "IX_LogTimeDepth_LoggingTimeDepthId",
                table: "LogTimeDepth",
                column: "LoggingTimeDepthId");

            migrationBuilder.CreateIndex(
                name: "IX_Magnetics_BTotal_Dip_Declination",
                table: "Magnetics",
                columns: new[] { "BTotal", "Dip", "Declination" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operator_Name",
                table: "Operator",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Passive_RunId",
                table: "Passive",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Passive_RunId_Order",
                table: "Passive",
                columns: new[] { "RunId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_PassiveComment_PassivesId",
                table: "PassiveComment",
                column: "PassivesId");

            migrationBuilder.CreateIndex(
                name: "IX_PassiveFile_PassiveId",
                table: "PassiveFile",
                column: "PassiveId");

            migrationBuilder.CreateIndex(
                name: "IX_PassiveLoggingProcessing_LoggingId",
                table: "PassiveLoggingProcessing",
                column: "LoggingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferencedJob_JobId",
                table: "ReferencedJob",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferencedJob_ReferencedTenantId_ReferencedJobId",
                table: "ReferencedJob",
                columns: new[] { "ReferencedTenantId", "ReferencedJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_Rotary_ParentId",
                table: "Rotary",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Rotary_RunId",
                table: "Rotary",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Rotary_RunId_Order",
                table: "Rotary",
                columns: new[] { "RunId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_RotaryComment_RotariesId",
                table: "RotaryComment",
                column: "RotariesId");

            migrationBuilder.CreateIndex(
                name: "IX_RotaryFile_RotaryId",
                table: "RotaryFile",
                column: "RotaryId");

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
                name: "IX_RotaryProcessing_LoggingId",
                table: "RotaryProcessing",
                column: "LoggingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RotarySolution_RotaryId",
                table: "RotarySolution",
                column: "RotaryId");

            migrationBuilder.CreateIndex(
                name: "IX_Run_JobId",
                table: "Run",
                column: "JobId");

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
                name: "IX_Shot_CalibrationsId",
                table: "Shot",
                column: "CalibrationsId");

            migrationBuilder.CreateIndex(
                name: "IX_Shot_GradientId",
                table: "Shot",
                column: "GradientId");

            migrationBuilder.CreateIndex(
                name: "IX_Shot_MagneticsId",
                table: "Shot",
                column: "MagneticsId");

            migrationBuilder.CreateIndex(
                name: "IX_Shot_RotaryId",
                table: "Shot",
                column: "RotaryId");

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
                name: "IX_ToolSurvey_ShotId",
                table: "ToolSurvey",
                column: "ShotId");

            migrationBuilder.CreateIndex(
                name: "IX_Tubular_WellId",
                table: "Tubular",
                column: "WellId");

            migrationBuilder.CreateIndex(
                name: "IX_Tubular_WellId_Order",
                table: "Tubular",
                columns: new[] { "WellId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveField");

            migrationBuilder.DropTable(
                name: "CommonMeasure");

            migrationBuilder.DropTable(
                name: "Formation");

            migrationBuilder.DropTable(
                name: "GradientComment");

            migrationBuilder.DropTable(
                name: "GradientFile");

            migrationBuilder.DropTable(
                name: "GradientModelRun");

            migrationBuilder.DropTable(
                name: "GradientSolution");

            migrationBuilder.DropTable(
                name: "GyroShot");

            migrationBuilder.DropTable(
                name: "JobUser");

            migrationBuilder.DropTable(
                name: "Log");

            migrationBuilder.DropTable(
                name: "LoggingEfd");

            migrationBuilder.DropTable(
                name: "LoggingFile");

            migrationBuilder.DropTable(
                name: "LoggingProcessing");

            migrationBuilder.DropTable(
                name: "LogTimeDepth");

            migrationBuilder.DropTable(
                name: "PassiveComment");

            migrationBuilder.DropTable(
                name: "PassiveFile");

            migrationBuilder.DropTable(
                name: "PassiveLoggingProcessing");

            migrationBuilder.DropTable(
                name: "ReferencedJob");

            migrationBuilder.DropTable(
                name: "RotaryComment");

            migrationBuilder.DropTable(
                name: "RotaryFile");

            migrationBuilder.DropTable(
                name: "RotaryModelRun");

            migrationBuilder.DropTable(
                name: "RotaryProcessing");

            migrationBuilder.DropTable(
                name: "RotarySolution");

            migrationBuilder.DropTable(
                name: "RunOperator");

            migrationBuilder.DropTable(
                name: "SavedGradientModel");

            migrationBuilder.DropTable(
                name: "Survey");

            migrationBuilder.DropTable(
                name: "TieOn");

            migrationBuilder.DropTable(
                name: "ToolSurvey");

            migrationBuilder.DropTable(
                name: "Tubular");

            migrationBuilder.DropTable(
                name: "LoggingTimeDepth");

            migrationBuilder.DropTable(
                name: "Passive");

            migrationBuilder.DropTable(
                name: "Comment");

            migrationBuilder.DropTable(
                name: "RotaryModel");

            migrationBuilder.DropTable(
                name: "Operator");

            migrationBuilder.DropTable(
                name: "GradientModel");

            migrationBuilder.DropTable(
                name: "Shot");

            migrationBuilder.DropTable(
                name: "Logging");

            migrationBuilder.DropTable(
                name: "Well");

            migrationBuilder.DropTable(
                name: "Gradient");

            migrationBuilder.DropTable(
                name: "Rotary");

            migrationBuilder.DropTable(
                name: "Calibration");

            migrationBuilder.DropTable(
                name: "LoggingSetting");

            migrationBuilder.DropTable(
                name: "Magnetics");

            migrationBuilder.DropTable(
                name: "Run");

            migrationBuilder.DropTable(
                name: "Job");
        }
    }
}

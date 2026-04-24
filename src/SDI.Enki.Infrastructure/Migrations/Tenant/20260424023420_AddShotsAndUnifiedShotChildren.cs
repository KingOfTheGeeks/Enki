using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddShotsAndUnifiedShotChildren : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Gradients",
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
                    table.PrimaryKey("PK_Gradients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Gradients_Gradients_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Gradients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Gradients_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Passives",
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
                    table.PrimaryKey("PK_Passives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Passives_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rotaries",
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
                    table.PrimaryKey("PK_Rotaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rotaries_Rotaries_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Rotaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rotaries_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Shots",
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
                    table.PrimaryKey("PK_Shots", x => x.Id);
                    table.CheckConstraint("CK_Shots_ExactlyOneParent", "([GradientId] IS NULL AND [RotaryId] IS NOT NULL) OR ([GradientId] IS NOT NULL AND [RotaryId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Shots_Calibrations_CalibrationsId",
                        column: x => x.CalibrationsId,
                        principalTable: "Calibrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shots_Gradients_GradientId",
                        column: x => x.GradientId,
                        principalTable: "Gradients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shots_Magnetics_MagneticsId",
                        column: x => x.MagneticsId,
                        principalTable: "Magnetics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shots_Rotaries_RotaryId",
                        column: x => x.RotaryId,
                        principalTable: "Rotaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActiveFields",
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
                    table.PrimaryKey("PK_ActiveFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActiveFields_Shots_ShotId",
                        column: x => x.ShotId,
                        principalTable: "Shots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GyroShots",
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
                    table.PrimaryKey("PK_GyroShots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GyroShots_Shots_ShotId",
                        column: x => x.ShotId,
                        principalTable: "Shots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToolSurveys",
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
                    table.PrimaryKey("PK_ToolSurveys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolSurveys_Shots_ShotId",
                        column: x => x.ShotId,
                        principalTable: "Shots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveFields_ShotId",
                table: "ActiveFields",
                column: "ShotId");

            migrationBuilder.CreateIndex(
                name: "IX_Gradients_ParentId",
                table: "Gradients",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Gradients_RunId",
                table: "Gradients",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Gradients_RunId_Order",
                table: "Gradients",
                columns: new[] { "RunId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_GyroShots_ShotId",
                table: "GyroShots",
                column: "ShotId");

            migrationBuilder.CreateIndex(
                name: "IX_Passives_RunId",
                table: "Passives",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Passives_RunId_Order",
                table: "Passives",
                columns: new[] { "RunId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Rotaries_ParentId",
                table: "Rotaries",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Rotaries_RunId",
                table: "Rotaries",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Rotaries_RunId_Order",
                table: "Rotaries",
                columns: new[] { "RunId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Shots_CalibrationsId",
                table: "Shots",
                column: "CalibrationsId");

            migrationBuilder.CreateIndex(
                name: "IX_Shots_GradientId",
                table: "Shots",
                column: "GradientId");

            migrationBuilder.CreateIndex(
                name: "IX_Shots_MagneticsId",
                table: "Shots",
                column: "MagneticsId");

            migrationBuilder.CreateIndex(
                name: "IX_Shots_RotaryId",
                table: "Shots",
                column: "RotaryId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolSurveys_ShotId",
                table: "ToolSurveys",
                column: "ShotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveFields");

            migrationBuilder.DropTable(
                name: "GyroShots");

            migrationBuilder.DropTable(
                name: "Passives");

            migrationBuilder.DropTable(
                name: "ToolSurveys");

            migrationBuilder.DropTable(
                name: "Shots");

            migrationBuilder.DropTable(
                name: "Gradients");

            migrationBuilder.DropTable(
                name: "Rotaries");
        }
    }
}

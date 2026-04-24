using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddSolutionsCommentsReferencesFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Comments",
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
                    table.PrimaryKey("PK_Comments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GradientFiles",
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
                    table.PrimaryKey("PK_GradientFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradientFiles_Gradients_GradientId",
                        column: x => x.GradientId,
                        principalTable: "Gradients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradientSolutions",
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
                    table.PrimaryKey("PK_GradientSolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradientSolutions_Gradients_GradientId",
                        column: x => x.GradientId,
                        principalTable: "Gradients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PassiveFiles",
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
                    table.PrimaryKey("PK_PassiveFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PassiveFiles_Passives_PassiveId",
                        column: x => x.PassiveId,
                        principalTable: "Passives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReferencedJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<int>(type: "int", nullable: false),
                    ReferencedTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferencedJobId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferencedJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferencedJobs_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotaryFiles",
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
                    table.PrimaryKey("PK_RotaryFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotaryFiles_Rotaries_RotaryId",
                        column: x => x.RotaryId,
                        principalTable: "Rotaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotarySolutions",
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
                    table.PrimaryKey("PK_RotarySolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotarySolutions_Rotaries_RotaryId",
                        column: x => x.RotaryId,
                        principalTable: "Rotaries",
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
                        name: "FK_GradientComment_Comments_CommentsId",
                        column: x => x.CommentsId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GradientComment_Gradients_GradientsId",
                        column: x => x.GradientsId,
                        principalTable: "Gradients",
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
                        name: "FK_PassiveComment_Comments_CommentsId",
                        column: x => x.CommentsId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PassiveComment_Passives_PassivesId",
                        column: x => x.PassivesId,
                        principalTable: "Passives",
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
                        name: "FK_RotaryComment_Comments_CommentsId",
                        column: x => x.CommentsId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RotaryComment_Rotaries_RotariesId",
                        column: x => x.RotariesId,
                        principalTable: "Rotaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GradientComment_GradientsId",
                table: "GradientComment",
                column: "GradientsId");

            migrationBuilder.CreateIndex(
                name: "IX_GradientFiles_GradientId",
                table: "GradientFiles",
                column: "GradientId");

            migrationBuilder.CreateIndex(
                name: "IX_GradientSolutions_GradientId",
                table: "GradientSolutions",
                column: "GradientId");

            migrationBuilder.CreateIndex(
                name: "IX_PassiveComment_PassivesId",
                table: "PassiveComment",
                column: "PassivesId");

            migrationBuilder.CreateIndex(
                name: "IX_PassiveFiles_PassiveId",
                table: "PassiveFiles",
                column: "PassiveId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferencedJobs_JobId",
                table: "ReferencedJobs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferencedJobs_ReferencedTenantId_ReferencedJobId",
                table: "ReferencedJobs",
                columns: new[] { "ReferencedTenantId", "ReferencedJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_RotaryComment_RotariesId",
                table: "RotaryComment",
                column: "RotariesId");

            migrationBuilder.CreateIndex(
                name: "IX_RotaryFiles_RotaryId",
                table: "RotaryFiles",
                column: "RotaryId");

            migrationBuilder.CreateIndex(
                name: "IX_RotarySolutions_RotaryId",
                table: "RotarySolutions",
                column: "RotaryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradientComment");

            migrationBuilder.DropTable(
                name: "GradientFiles");

            migrationBuilder.DropTable(
                name: "GradientSolutions");

            migrationBuilder.DropTable(
                name: "PassiveComment");

            migrationBuilder.DropTable(
                name: "PassiveFiles");

            migrationBuilder.DropTable(
                name: "ReferencedJobs");

            migrationBuilder.DropTable(
                name: "RotaryComment");

            migrationBuilder.DropTable(
                name: "RotaryFiles");

            migrationBuilder.DropTable(
                name: "RotarySolutions");

            migrationBuilder.DropTable(
                name: "Comments");
        }
    }
}

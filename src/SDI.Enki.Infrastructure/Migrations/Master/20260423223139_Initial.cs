using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SDI.Enki.Infrastructure.Migrations.Master
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TargetVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    JsonObject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ObjectClass = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<int>(type: "int", nullable: false),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Configuration = table.Column<int>(type: "int", nullable: false),
                    Size = table.Column<int>(type: "int", nullable: false),
                    MagnetometerCount = table.Column<int>(type: "int", nullable: false),
                    AccelerometerCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantDatabases",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ServerInstance = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastMigrationAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastBackupAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantDatabases", x => new { x.TenantId, x.Kind });
                    table.ForeignKey(
                        name: "FK_TenantDatabases_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Calibrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<int>(type: "int", nullable: false),
                    CalibrationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CalibratedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calibrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Calibrations_Tools_ToolId",
                        column: x => x.ToolId,
                        principalTable: "Tools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SettingUser",
                columns: table => new
                {
                    SettingId = table.Column<int>(type: "int", nullable: false),
                    UsersId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingUser", x => new { x.SettingId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_SettingUser_Settings_SettingId",
                        column: x => x.SettingId,
                        principalTable: "Settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SettingUser_Users_UsersId",
                        column: x => x.UsersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantUsers",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantUsers", x => new { x.TenantId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TenantUsers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserUserTemplate",
                columns: table => new
                {
                    TemplatesId = table.Column<int>(type: "int", nullable: false),
                    UsersId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserUserTemplate", x => new { x.TemplatesId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_UserUserTemplate_UserTemplates_TemplatesId",
                        column: x => x.TemplatesId,
                        principalTable: "UserTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserUserTemplate_Users_UsersId",
                        column: x => x.UsersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "UserTemplates",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { 1, "Default security template; contains access for all team members.", "All Team Access" },
                    { 2, "Technical security template; contains access for technical team members.", "Technical Team Access" },
                    { 3, "Senior security template; contains access for senior team members.", "Senior Team Access" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "IdentityId", "Name" },
                values: new object[,]
                {
                    { new Guid("02c9751a-3058-4e15-b5c5-ce82adaebaeb"), new Guid("bd34385d-2d88-4781-bef5-e955ddaa8293"), "douglas.ridgway" },
                    { new Guid("050add37-54b3-4996-9bcc-8ed3cc4992b6"), new Guid("bc120086-fc2d-4f41-b76a-3f6c3536c2cc"), "scott.brandel" },
                    { new Guid("0c2c609c-abb0-4009-8928-e274352caf11"), new Guid("d92be0d5-dfbe-4d1d-9823-1ca37617dade"), "john.borders" },
                    { new Guid("123505bf-cd91-4e15-b583-ad1291347508"), new Guid("e5a7f984-688a-4904-8155-3fe724584385"), "travis.solomon" },
                    { new Guid("466ba5fd-d339-4a92-93bc-ec3354a98945"), new Guid("2c4f110e-adc4-4759-aa34-b73ec0954c9e"), "gavin.helboe" },
                    { new Guid("7a519cae-da73-41df-82dd-05fbc8bc73a0"), new Guid("8cf4b730-c619-49d0-8ed7-be0ac89de718"), "dapo.ajayi" },
                    { new Guid("ab3f526a-849b-492b-91d9-f3851e978869"), new Guid("dafd065f-4790-4235-9db0-6f47abadf3aa"), "adam.karabasz" },
                    { new Guid("ce17bb43-1eac-439e-80a5-324a3edaf373"), new Guid("a72f07d8-9a12-4825-95f4-7c5bbea6e6e5"), "james.powell" },
                    { new Guid("e48bacc4-4375-4445-88b0-e08c20216513"), new Guid("f8d3ceda-ce98-4825-88f9-c8e8356a61db"), "joel.harrison" },
                    { new Guid("e8dd0c2a-bceb-4885-a90e-8f9cf446ee5a"), new Guid("f8aff5b3-473b-436f-9592-186cb28ac848"), "jamie.dorey" },
                    { new Guid("f5fd1207-1dc6-49c7-a794-b5420bd88008"), new Guid("1e333b45-1448-4b26-a68d-b4effbbdcd9d"), "mike.king" },
                    { new Guid("f9830a6a-d787-4333-9e66-aa03c9a58b51"), new Guid("92473a14-0196-42ed-b098-9c3d85505f8d"), "karl.king" }
                });

            migrationBuilder.InsertData(
                table: "UserUserTemplate",
                columns: new[] { "TemplatesId", "UsersId" },
                values: new object[,]
                {
                    { 1, new Guid("02c9751a-3058-4e15-b5c5-ce82adaebaeb") },
                    { 1, new Guid("050add37-54b3-4996-9bcc-8ed3cc4992b6") },
                    { 1, new Guid("0c2c609c-abb0-4009-8928-e274352caf11") },
                    { 1, new Guid("123505bf-cd91-4e15-b583-ad1291347508") },
                    { 1, new Guid("466ba5fd-d339-4a92-93bc-ec3354a98945") },
                    { 1, new Guid("7a519cae-da73-41df-82dd-05fbc8bc73a0") },
                    { 1, new Guid("ab3f526a-849b-492b-91d9-f3851e978869") },
                    { 1, new Guid("ce17bb43-1eac-439e-80a5-324a3edaf373") },
                    { 1, new Guid("e48bacc4-4375-4445-88b0-e08c20216513") },
                    { 1, new Guid("e8dd0c2a-bceb-4885-a90e-8f9cf446ee5a") },
                    { 1, new Guid("f5fd1207-1dc6-49c7-a794-b5420bd88008") },
                    { 1, new Guid("f9830a6a-d787-4333-9e66-aa03c9a58b51") },
                    { 2, new Guid("02c9751a-3058-4e15-b5c5-ce82adaebaeb") },
                    { 2, new Guid("466ba5fd-d339-4a92-93bc-ec3354a98945") },
                    { 2, new Guid("f5fd1207-1dc6-49c7-a794-b5420bd88008") },
                    { 2, new Guid("f9830a6a-d787-4333-9e66-aa03c9a58b51") },
                    { 3, new Guid("e48bacc4-4375-4445-88b0-e08c20216513") },
                    { 3, new Guid("e8dd0c2a-bceb-4885-a90e-8f9cf446ee5a") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Calibrations_SerialNumber",
                table: "Calibrations",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Calibrations_ToolId",
                table: "Calibrations",
                column: "ToolId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationRuns_StartedAt",
                table: "MigrationRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationRuns_TenantId",
                table: "MigrationRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingUser_UsersId",
                table: "SettingUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Code",
                table: "Tenants",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantUsers_UserId",
                table: "TenantUsers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tools_SerialNumber",
                table: "Tools",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserUserTemplate_UsersId",
                table: "UserUserTemplate",
                column: "UsersId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Calibrations");

            migrationBuilder.DropTable(
                name: "MigrationRuns");

            migrationBuilder.DropTable(
                name: "SettingUser");

            migrationBuilder.DropTable(
                name: "TenantDatabases");

            migrationBuilder.DropTable(
                name: "TenantUsers");

            migrationBuilder.DropTable(
                name: "UserUserTemplate");

            migrationBuilder.DropTable(
                name: "Tools");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "UserTemplates");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}

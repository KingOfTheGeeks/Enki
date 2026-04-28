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
                name: "MigrationRun",
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
                    table.PrimaryKey("PK_MigrationRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Setting",
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
                    table.PrimaryKey("PK_Setting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSetting",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tool",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<int>(type: "int", nullable: false),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Configuration = table.Column<int>(type: "int", nullable: false),
                    Size = table.Column<int>(type: "int", nullable: false),
                    MagnetometerCount = table.Column<int>(type: "int", nullable: false),
                    AccelerometerCount = table.Column<int>(type: "int", nullable: false),
                    Generation = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tool", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserTemplate",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantDatabase",
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
                    LastBackupAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantDatabase", x => new { x.TenantId, x.Kind });
                    table.ForeignKey(
                        name: "FK_TenantDatabase_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Calibration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<int>(type: "int", nullable: false),
                    CalibrationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CalibratedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MagnetometerCount = table.Column<int>(type: "int", nullable: false),
                    IsNominal = table.Column<bool>(type: "bit", nullable: false),
                    IsSuperseded = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calibration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Calibration_Tool_ToolId",
                        column: x => x.ToolId,
                        principalTable: "Tool",
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
                        name: "FK_SettingUser_Setting_SettingId",
                        column: x => x.SettingId,
                        principalTable: "Setting",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SettingUser_User_UsersId",
                        column: x => x.UsersId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantUser",
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
                    table.PrimaryKey("PK_TenantUser", x => new { x.TenantId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TenantUser_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantUser_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
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
                        name: "FK_UserUserTemplate_UserTemplate_TemplatesId",
                        column: x => x.TemplatesId,
                        principalTable: "UserTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserUserTemplate_User_UsersId",
                        column: x => x.UsersId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SystemSetting",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "Key", "UpdatedAt", "UpdatedBy", "Value" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 4, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Jobs:RegionSuggestions", null, null, "Permian Basin\nBakken\nEagle Ford\nHaynesville\nMarcellus\nNorth Sea\nGulf of Mexico\nMiddle East\nNorth Slope\nWestern Australia" });

            migrationBuilder.InsertData(
                table: "User",
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
                    { new Guid("f5fd1207-1dc6-49c7-a794-b5420bd88008"), new Guid("1e333b45-1448-4b26-a68d-b4effbbdcd9d"), "mike.king" }
                });

            migrationBuilder.InsertData(
                table: "UserTemplate",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { 1, "Default security template; contains access for all team members.", "All Team Access" },
                    { 2, "Technical security template; contains access for technical team members.", "Technical Team Access" },
                    { 3, "Senior security template; contains access for senior team members.", "Senior Team Access" }
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
                    { 2, new Guid("02c9751a-3058-4e15-b5c5-ce82adaebaeb") },
                    { 2, new Guid("466ba5fd-d339-4a92-93bc-ec3354a98945") },
                    { 2, new Guid("f5fd1207-1dc6-49c7-a794-b5420bd88008") },
                    { 3, new Guid("e48bacc4-4375-4445-88b0-e08c20216513") },
                    { 3, new Guid("e8dd0c2a-bceb-4885-a90e-8f9cf446ee5a") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Calibration_SerialNumber",
                table: "Calibration",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Calibration_ToolId",
                table: "Calibration",
                column: "ToolId");

            migrationBuilder.CreateIndex(
                name: "IX_Calibration_ToolId_IsSuperseded",
                table: "Calibration",
                columns: new[] { "ToolId", "IsSuperseded" });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationRun_StartedAt",
                table: "MigrationRun",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationRun_TenantId",
                table: "MigrationRun",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingUser_UsersId",
                table: "SettingUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSetting_Key",
                table: "SystemSetting",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenant_Code",
                table: "Tenant",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantUser_UserId",
                table: "TenantUser",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tool_Generation",
                table: "Tool",
                column: "Generation");

            migrationBuilder.CreateIndex(
                name: "IX_Tool_SerialNumber",
                table: "Tool",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tool_Status",
                table: "Tool",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_UserUserTemplate_UsersId",
                table: "UserUserTemplate",
                column: "UsersId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Calibration");

            migrationBuilder.DropTable(
                name: "MigrationRun");

            migrationBuilder.DropTable(
                name: "SettingUser");

            migrationBuilder.DropTable(
                name: "SystemSetting");

            migrationBuilder.DropTable(
                name: "TenantDatabase");

            migrationBuilder.DropTable(
                name: "TenantUser");

            migrationBuilder.DropTable(
                name: "UserUserTemplate");

            migrationBuilder.DropTable(
                name: "Tool");

            migrationBuilder.DropTable(
                name: "Setting");

            migrationBuilder.DropTable(
                name: "Tenant");

            migrationBuilder.DropTable(
                name: "UserTemplate");

            migrationBuilder.DropTable(
                name: "User");
        }
    }
}

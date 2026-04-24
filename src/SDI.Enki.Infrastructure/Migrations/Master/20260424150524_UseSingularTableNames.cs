using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Master
{
    /// <inheritdoc />
    public partial class UseSingularTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Calibrations_Tools_ToolId",
                table: "Calibrations");

            migrationBuilder.DropForeignKey(
                name: "FK_SettingUser_Settings_SettingId",
                table: "SettingUser");

            migrationBuilder.DropForeignKey(
                name: "FK_SettingUser_Users_UsersId",
                table: "SettingUser");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantDatabases_Tenants_TenantId",
                table: "TenantDatabases");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantUsers_Tenants_TenantId",
                table: "TenantUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantUsers_Users_UserId",
                table: "TenantUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_UserUserTemplate_UserTemplates_TemplatesId",
                table: "UserUserTemplate");

            migrationBuilder.DropForeignKey(
                name: "FK_UserUserTemplate_Users_UsersId",
                table: "UserUserTemplate");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserTemplates",
                table: "UserTemplates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Users",
                table: "Users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tools",
                table: "Tools");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TenantUsers",
                table: "TenantUsers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tenants",
                table: "Tenants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TenantDatabases",
                table: "TenantDatabases");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                table: "Settings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MigrationRuns",
                table: "MigrationRuns");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Calibrations",
                table: "Calibrations");

            migrationBuilder.RenameTable(
                name: "UserTemplates",
                newName: "UserTemplate");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "User");

            migrationBuilder.RenameTable(
                name: "Tools",
                newName: "Tool");

            migrationBuilder.RenameTable(
                name: "TenantUsers",
                newName: "TenantUser");

            migrationBuilder.RenameTable(
                name: "Tenants",
                newName: "Tenant");

            migrationBuilder.RenameTable(
                name: "TenantDatabases",
                newName: "TenantDatabase");

            migrationBuilder.RenameTable(
                name: "Settings",
                newName: "Setting");

            migrationBuilder.RenameTable(
                name: "MigrationRuns",
                newName: "MigrationRun");

            migrationBuilder.RenameTable(
                name: "Calibrations",
                newName: "Calibration");

            migrationBuilder.RenameIndex(
                name: "IX_Tools_SerialNumber",
                table: "Tool",
                newName: "IX_Tool_SerialNumber");

            migrationBuilder.RenameIndex(
                name: "IX_TenantUsers_UserId",
                table: "TenantUser",
                newName: "IX_TenantUser_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Tenants_Code",
                table: "Tenant",
                newName: "IX_Tenant_Code");

            migrationBuilder.RenameIndex(
                name: "IX_MigrationRuns_TenantId",
                table: "MigrationRun",
                newName: "IX_MigrationRun_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_MigrationRuns_StartedAt",
                table: "MigrationRun",
                newName: "IX_MigrationRun_StartedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Calibrations_ToolId",
                table: "Calibration",
                newName: "IX_Calibration_ToolId");

            migrationBuilder.RenameIndex(
                name: "IX_Calibrations_SerialNumber",
                table: "Calibration",
                newName: "IX_Calibration_SerialNumber");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserTemplate",
                table: "UserTemplate",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_User",
                table: "User",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tool",
                table: "Tool",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TenantUser",
                table: "TenantUser",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tenant",
                table: "Tenant",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TenantDatabase",
                table: "TenantDatabase",
                columns: new[] { "TenantId", "Kind" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Setting",
                table: "Setting",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MigrationRun",
                table: "MigrationRun",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Calibration",
                table: "Calibration",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Calibration_Tool_ToolId",
                table: "Calibration",
                column: "ToolId",
                principalTable: "Tool",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SettingUser_Setting_SettingId",
                table: "SettingUser",
                column: "SettingId",
                principalTable: "Setting",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SettingUser_User_UsersId",
                table: "SettingUser",
                column: "UsersId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantDatabase_Tenant_TenantId",
                table: "TenantDatabase",
                column: "TenantId",
                principalTable: "Tenant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantUser_Tenant_TenantId",
                table: "TenantUser",
                column: "TenantId",
                principalTable: "Tenant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantUser_User_UserId",
                table: "TenantUser",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserUserTemplate_UserTemplate_TemplatesId",
                table: "UserUserTemplate",
                column: "TemplatesId",
                principalTable: "UserTemplate",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserUserTemplate_User_UsersId",
                table: "UserUserTemplate",
                column: "UsersId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Calibration_Tool_ToolId",
                table: "Calibration");

            migrationBuilder.DropForeignKey(
                name: "FK_SettingUser_Setting_SettingId",
                table: "SettingUser");

            migrationBuilder.DropForeignKey(
                name: "FK_SettingUser_User_UsersId",
                table: "SettingUser");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantDatabase_Tenant_TenantId",
                table: "TenantDatabase");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantUser_Tenant_TenantId",
                table: "TenantUser");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantUser_User_UserId",
                table: "TenantUser");

            migrationBuilder.DropForeignKey(
                name: "FK_UserUserTemplate_UserTemplate_TemplatesId",
                table: "UserUserTemplate");

            migrationBuilder.DropForeignKey(
                name: "FK_UserUserTemplate_User_UsersId",
                table: "UserUserTemplate");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserTemplate",
                table: "UserTemplate");

            migrationBuilder.DropPrimaryKey(
                name: "PK_User",
                table: "User");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tool",
                table: "Tool");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TenantUser",
                table: "TenantUser");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TenantDatabase",
                table: "TenantDatabase");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tenant",
                table: "Tenant");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Setting",
                table: "Setting");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MigrationRun",
                table: "MigrationRun");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Calibration",
                table: "Calibration");

            migrationBuilder.RenameTable(
                name: "UserTemplate",
                newName: "UserTemplates");

            migrationBuilder.RenameTable(
                name: "User",
                newName: "Users");

            migrationBuilder.RenameTable(
                name: "Tool",
                newName: "Tools");

            migrationBuilder.RenameTable(
                name: "TenantUser",
                newName: "TenantUsers");

            migrationBuilder.RenameTable(
                name: "TenantDatabase",
                newName: "TenantDatabases");

            migrationBuilder.RenameTable(
                name: "Tenant",
                newName: "Tenants");

            migrationBuilder.RenameTable(
                name: "Setting",
                newName: "Settings");

            migrationBuilder.RenameTable(
                name: "MigrationRun",
                newName: "MigrationRuns");

            migrationBuilder.RenameTable(
                name: "Calibration",
                newName: "Calibrations");

            migrationBuilder.RenameIndex(
                name: "IX_Tool_SerialNumber",
                table: "Tools",
                newName: "IX_Tools_SerialNumber");

            migrationBuilder.RenameIndex(
                name: "IX_TenantUser_UserId",
                table: "TenantUsers",
                newName: "IX_TenantUsers_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Tenant_Code",
                table: "Tenants",
                newName: "IX_Tenants_Code");

            migrationBuilder.RenameIndex(
                name: "IX_MigrationRun_TenantId",
                table: "MigrationRuns",
                newName: "IX_MigrationRuns_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_MigrationRun_StartedAt",
                table: "MigrationRuns",
                newName: "IX_MigrationRuns_StartedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Calibration_ToolId",
                table: "Calibrations",
                newName: "IX_Calibrations_ToolId");

            migrationBuilder.RenameIndex(
                name: "IX_Calibration_SerialNumber",
                table: "Calibrations",
                newName: "IX_Calibrations_SerialNumber");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserTemplates",
                table: "UserTemplates",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Users",
                table: "Users",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tools",
                table: "Tools",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TenantUsers",
                table: "TenantUsers",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_TenantDatabases",
                table: "TenantDatabases",
                columns: new[] { "TenantId", "Kind" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tenants",
                table: "Tenants",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                table: "Settings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MigrationRuns",
                table: "MigrationRuns",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Calibrations",
                table: "Calibrations",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Calibrations_Tools_ToolId",
                table: "Calibrations",
                column: "ToolId",
                principalTable: "Tools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SettingUser_Settings_SettingId",
                table: "SettingUser",
                column: "SettingId",
                principalTable: "Settings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SettingUser_Users_UsersId",
                table: "SettingUser",
                column: "UsersId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantDatabases_Tenants_TenantId",
                table: "TenantDatabases",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantUsers_Tenants_TenantId",
                table: "TenantUsers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantUsers_Users_UserId",
                table: "TenantUsers",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserUserTemplate_UserTemplates_TemplatesId",
                table: "UserUserTemplate",
                column: "TemplatesId",
                principalTable: "UserTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserUserTemplate_Users_UsersId",
                table: "UserUserTemplate",
                column: "UsersId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

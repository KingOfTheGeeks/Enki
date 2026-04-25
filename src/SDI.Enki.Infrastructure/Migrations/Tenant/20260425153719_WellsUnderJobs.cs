using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class WellsUnderJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "Well",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Well_JobId",
                table: "Well",
                column: "JobId");

            migrationBuilder.AddForeignKey(
                name: "FK_Well_Job_JobId",
                table: "Well",
                column: "JobId",
                principalTable: "Job",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Well_Job_JobId",
                table: "Well");

            migrationBuilder.DropIndex(
                name: "IX_Well_JobId",
                table: "Well");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "Well");
        }
    }
}

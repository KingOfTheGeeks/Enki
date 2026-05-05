using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class RenameInjectionToInterceptInModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GradientModel_Well_InjectionWellId",
                table: "GradientModel");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModel_Well_InjectionWellId",
                table: "RotaryModel");

            migrationBuilder.RenameColumn(
                name: "InjectionWellId",
                table: "RotaryModel",
                newName: "InterceptWellId");

            migrationBuilder.RenameIndex(
                name: "IX_RotaryModel_InjectionWellId",
                table: "RotaryModel",
                newName: "IX_RotaryModel_InterceptWellId");

            migrationBuilder.RenameColumn(
                name: "InjectionWellId",
                table: "GradientModel",
                newName: "InterceptWellId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientModel_InjectionWellId",
                table: "GradientModel",
                newName: "IX_GradientModel_InterceptWellId");

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModel_Well_InterceptWellId",
                table: "GradientModel",
                column: "InterceptWellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModel_Well_InterceptWellId",
                table: "RotaryModel",
                column: "InterceptWellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GradientModel_Well_InterceptWellId",
                table: "GradientModel");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModel_Well_InterceptWellId",
                table: "RotaryModel");

            migrationBuilder.RenameColumn(
                name: "InterceptWellId",
                table: "RotaryModel",
                newName: "InjectionWellId");

            migrationBuilder.RenameIndex(
                name: "IX_RotaryModel_InterceptWellId",
                table: "RotaryModel",
                newName: "IX_RotaryModel_InjectionWellId");

            migrationBuilder.RenameColumn(
                name: "InterceptWellId",
                table: "GradientModel",
                newName: "InjectionWellId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientModel_InterceptWellId",
                table: "GradientModel",
                newName: "IX_GradientModel_InjectionWellId");

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModel_Well_InjectionWellId",
                table: "GradientModel",
                column: "InjectionWellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModel_Well_InjectionWellId",
                table: "RotaryModel",
                column: "InjectionWellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

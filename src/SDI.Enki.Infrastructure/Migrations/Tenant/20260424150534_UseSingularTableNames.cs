using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDI.Enki.Infrastructure.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class UseSingularTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActiveFields_Shots_ShotId",
                table: "ActiveFields");

            migrationBuilder.DropForeignKey(
                name: "FK_CommonMeasures_Wells_WellId",
                table: "CommonMeasures");

            migrationBuilder.DropForeignKey(
                name: "FK_Formations_Wells_WellId",
                table: "Formations");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientComment_Comments_CommentsId",
                table: "GradientComment");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientComment_Gradients_GradientsId",
                table: "GradientComment");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientFiles_Gradients_GradientId",
                table: "GradientFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientModelRun_GradientModels_GradientModelsId",
                table: "GradientModelRun");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientModelRun_Runs_RunsId",
                table: "GradientModelRun");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientModels_Wells_InjectionWellId",
                table: "GradientModels");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientModels_Wells_TargetWellId",
                table: "GradientModels");

            migrationBuilder.DropForeignKey(
                name: "FK_Gradients_Gradients_ParentId",
                table: "Gradients");

            migrationBuilder.DropForeignKey(
                name: "FK_Gradients_Runs_RunId",
                table: "Gradients");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientSolutions_Gradients_GradientId",
                table: "GradientSolutions");

            migrationBuilder.DropForeignKey(
                name: "FK_GyroShots_Shots_ShotId",
                table: "GyroShots");

            migrationBuilder.DropForeignKey(
                name: "FK_JobUsers_Jobs_JobId",
                table: "JobUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_LoggingEfd_Loggings_LoggingId",
                table: "LoggingEfd");

            migrationBuilder.DropForeignKey(
                name: "FK_LoggingFiles_Loggings_LoggingId",
                table: "LoggingFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_LoggingProcessing_Loggings_LoggingId",
                table: "LoggingProcessing");

            migrationBuilder.DropForeignKey(
                name: "FK_Loggings_Calibrations_CalibrationId",
                table: "Loggings");

            migrationBuilder.DropForeignKey(
                name: "FK_Loggings_LoggingSettings_LogSettingId",
                table: "Loggings");

            migrationBuilder.DropForeignKey(
                name: "FK_Loggings_Magnetics_MagneticId",
                table: "Loggings");

            migrationBuilder.DropForeignKey(
                name: "FK_Loggings_Runs_GradientRunId",
                table: "Loggings");

            migrationBuilder.DropForeignKey(
                name: "FK_Loggings_Runs_PassiveRunId",
                table: "Loggings");

            migrationBuilder.DropForeignKey(
                name: "FK_Loggings_Runs_RotaryRunId",
                table: "Loggings");

            migrationBuilder.DropForeignKey(
                name: "FK_LoggingTimeDepth_Loggings_LoggingId",
                table: "LoggingTimeDepth");

            migrationBuilder.DropForeignKey(
                name: "FK_Logs_Loggings_LoggingId",
                table: "Logs");

            migrationBuilder.DropForeignKey(
                name: "FK_PassiveComment_Comments_CommentsId",
                table: "PassiveComment");

            migrationBuilder.DropForeignKey(
                name: "FK_PassiveComment_Passives_PassivesId",
                table: "PassiveComment");

            migrationBuilder.DropForeignKey(
                name: "FK_PassiveFiles_Passives_PassiveId",
                table: "PassiveFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_PassiveLoggingProcessing_Loggings_LoggingId",
                table: "PassiveLoggingProcessing");

            migrationBuilder.DropForeignKey(
                name: "FK_Passives_Runs_RunId",
                table: "Passives");

            migrationBuilder.DropForeignKey(
                name: "FK_ReferencedJobs_Jobs_JobId",
                table: "ReferencedJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_Rotaries_Rotaries_ParentId",
                table: "Rotaries");

            migrationBuilder.DropForeignKey(
                name: "FK_Rotaries_Runs_RunId",
                table: "Rotaries");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryComment_Comments_CommentsId",
                table: "RotaryComment");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryComment_Rotaries_RotariesId",
                table: "RotaryComment");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryFiles_Rotaries_RotaryId",
                table: "RotaryFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModelRun_RotaryModels_RotaryModelsId",
                table: "RotaryModelRun");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModelRun_Runs_RunsId",
                table: "RotaryModelRun");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModels_Wells_InjectionWellId",
                table: "RotaryModels");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModels_Wells_TargetWellId",
                table: "RotaryModels");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryProcessing_Loggings_LoggingId",
                table: "RotaryProcessing");

            migrationBuilder.DropForeignKey(
                name: "FK_RotarySolutions_Rotaries_RotaryId",
                table: "RotarySolutions");

            migrationBuilder.DropForeignKey(
                name: "FK_RunOperator_Operators_OperatorsId",
                table: "RunOperator");

            migrationBuilder.DropForeignKey(
                name: "FK_RunOperator_Runs_RunsId",
                table: "RunOperator");

            migrationBuilder.DropForeignKey(
                name: "FK_Runs_Jobs_JobId",
                table: "Runs");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedGradientModels_GradientModels_GradientModelId",
                table: "SavedGradientModels");

            migrationBuilder.DropForeignKey(
                name: "FK_Shots_Calibrations_CalibrationsId",
                table: "Shots");

            migrationBuilder.DropForeignKey(
                name: "FK_Shots_Gradients_GradientId",
                table: "Shots");

            migrationBuilder.DropForeignKey(
                name: "FK_Shots_Magnetics_MagneticsId",
                table: "Shots");

            migrationBuilder.DropForeignKey(
                name: "FK_Shots_Rotaries_RotaryId",
                table: "Shots");

            migrationBuilder.DropForeignKey(
                name: "FK_Surveys_Wells_WellId",
                table: "Surveys");

            migrationBuilder.DropForeignKey(
                name: "FK_TieOns_Wells_WellId",
                table: "TieOns");

            migrationBuilder.DropForeignKey(
                name: "FK_ToolSurveys_Shots_ShotId",
                table: "ToolSurveys");

            migrationBuilder.DropForeignKey(
                name: "FK_Tubulars_Wells_WellId",
                table: "Tubulars");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Wells",
                table: "Wells");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tubulars",
                table: "Tubulars");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ToolSurveys",
                table: "ToolSurveys");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TieOns",
                table: "TieOns");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Surveys",
                table: "Surveys");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Shots",
                table: "Shots");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SavedGradientModels",
                table: "SavedGradientModels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Runs",
                table: "Runs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RotarySolutions",
                table: "RotarySolutions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RotaryModels",
                table: "RotaryModels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RotaryFiles",
                table: "RotaryFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Rotaries",
                table: "Rotaries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReferencedJobs",
                table: "ReferencedJobs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Passives",
                table: "Passives");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PassiveFiles",
                table: "PassiveFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Operators",
                table: "Operators");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Logs",
                table: "Logs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LoggingSettings",
                table: "LoggingSettings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Loggings",
                table: "Loggings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LoggingFiles",
                table: "LoggingFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_JobUsers",
                table: "JobUsers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Jobs",
                table: "Jobs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GyroShots",
                table: "GyroShots");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GradientSolutions",
                table: "GradientSolutions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Gradients",
                table: "Gradients");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GradientModels",
                table: "GradientModels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GradientFiles",
                table: "GradientFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Formations",
                table: "Formations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CommonMeasures",
                table: "CommonMeasures");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Comments",
                table: "Comments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Calibrations",
                table: "Calibrations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ActiveFields",
                table: "ActiveFields");

            migrationBuilder.RenameTable(
                name: "Wells",
                newName: "Well");

            migrationBuilder.RenameTable(
                name: "Tubulars",
                newName: "Tubular");

            migrationBuilder.RenameTable(
                name: "ToolSurveys",
                newName: "ToolSurvey");

            migrationBuilder.RenameTable(
                name: "TieOns",
                newName: "TieOn");

            migrationBuilder.RenameTable(
                name: "Surveys",
                newName: "Survey");

            migrationBuilder.RenameTable(
                name: "Shots",
                newName: "Shot");

            migrationBuilder.RenameTable(
                name: "SavedGradientModels",
                newName: "SavedGradientModel");

            migrationBuilder.RenameTable(
                name: "Runs",
                newName: "Run");

            migrationBuilder.RenameTable(
                name: "RotarySolutions",
                newName: "RotarySolution");

            migrationBuilder.RenameTable(
                name: "RotaryModels",
                newName: "RotaryModel");

            migrationBuilder.RenameTable(
                name: "RotaryFiles",
                newName: "RotaryFile");

            migrationBuilder.RenameTable(
                name: "Rotaries",
                newName: "Rotary");

            migrationBuilder.RenameTable(
                name: "ReferencedJobs",
                newName: "ReferencedJob");

            migrationBuilder.RenameTable(
                name: "Passives",
                newName: "Passive");

            migrationBuilder.RenameTable(
                name: "PassiveFiles",
                newName: "PassiveFile");

            migrationBuilder.RenameTable(
                name: "Operators",
                newName: "Operator");

            migrationBuilder.RenameTable(
                name: "Logs",
                newName: "Log");

            migrationBuilder.RenameTable(
                name: "LoggingSettings",
                newName: "LoggingSetting");

            migrationBuilder.RenameTable(
                name: "Loggings",
                newName: "Logging");

            migrationBuilder.RenameTable(
                name: "LoggingFiles",
                newName: "LoggingFile");

            migrationBuilder.RenameTable(
                name: "JobUsers",
                newName: "JobUser");

            migrationBuilder.RenameTable(
                name: "Jobs",
                newName: "Job");

            migrationBuilder.RenameTable(
                name: "GyroShots",
                newName: "GyroShot");

            migrationBuilder.RenameTable(
                name: "GradientSolutions",
                newName: "GradientSolution");

            migrationBuilder.RenameTable(
                name: "Gradients",
                newName: "Gradient");

            migrationBuilder.RenameTable(
                name: "GradientModels",
                newName: "GradientModel");

            migrationBuilder.RenameTable(
                name: "GradientFiles",
                newName: "GradientFile");

            migrationBuilder.RenameTable(
                name: "Formations",
                newName: "Formation");

            migrationBuilder.RenameTable(
                name: "CommonMeasures",
                newName: "CommonMeasure");

            migrationBuilder.RenameTable(
                name: "Comments",
                newName: "Comment");

            migrationBuilder.RenameTable(
                name: "Calibrations",
                newName: "Calibration");

            migrationBuilder.RenameTable(
                name: "ActiveFields",
                newName: "ActiveField");

            migrationBuilder.RenameIndex(
                name: "IX_Tubulars_WellId_Order",
                table: "Tubular",
                newName: "IX_Tubular_WellId_Order");

            migrationBuilder.RenameIndex(
                name: "IX_Tubulars_WellId",
                table: "Tubular",
                newName: "IX_Tubular_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_ToolSurveys_ShotId",
                table: "ToolSurvey",
                newName: "IX_ToolSurvey_ShotId");

            migrationBuilder.RenameIndex(
                name: "IX_TieOns_WellId",
                table: "TieOn",
                newName: "IX_TieOn_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_Surveys_WellId_Depth",
                table: "Survey",
                newName: "IX_Survey_WellId_Depth");

            migrationBuilder.RenameIndex(
                name: "IX_Surveys_WellId",
                table: "Survey",
                newName: "IX_Survey_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_Shots_RotaryId",
                table: "Shot",
                newName: "IX_Shot_RotaryId");

            migrationBuilder.RenameIndex(
                name: "IX_Shots_MagneticsId",
                table: "Shot",
                newName: "IX_Shot_MagneticsId");

            migrationBuilder.RenameIndex(
                name: "IX_Shots_GradientId",
                table: "Shot",
                newName: "IX_Shot_GradientId");

            migrationBuilder.RenameIndex(
                name: "IX_Shots_CalibrationsId",
                table: "Shot",
                newName: "IX_Shot_CalibrationsId");

            migrationBuilder.RenameIndex(
                name: "IX_SavedGradientModels_GradientModelId",
                table: "SavedGradientModel",
                newName: "IX_SavedGradientModel_GradientModelId");

            migrationBuilder.RenameIndex(
                name: "IX_SavedGradientModels_CreationTime",
                table: "SavedGradientModel",
                newName: "IX_SavedGradientModel_CreationTime");

            migrationBuilder.RenameIndex(
                name: "IX_Runs_Type",
                table: "Run",
                newName: "IX_Run_Type");

            migrationBuilder.RenameIndex(
                name: "IX_Runs_JobId",
                table: "Run",
                newName: "IX_Run_JobId");

            migrationBuilder.RenameIndex(
                name: "IX_RotarySolutions_RotaryId",
                table: "RotarySolution",
                newName: "IX_RotarySolution_RotaryId");

            migrationBuilder.RenameIndex(
                name: "IX_RotaryModels_TargetWellId",
                table: "RotaryModel",
                newName: "IX_RotaryModel_TargetWellId");

            migrationBuilder.RenameIndex(
                name: "IX_RotaryModels_InjectionWellId",
                table: "RotaryModel",
                newName: "IX_RotaryModel_InjectionWellId");

            migrationBuilder.RenameIndex(
                name: "IX_RotaryFiles_RotaryId",
                table: "RotaryFile",
                newName: "IX_RotaryFile_RotaryId");

            migrationBuilder.RenameIndex(
                name: "IX_Rotaries_RunId_Order",
                table: "Rotary",
                newName: "IX_Rotary_RunId_Order");

            migrationBuilder.RenameIndex(
                name: "IX_Rotaries_RunId",
                table: "Rotary",
                newName: "IX_Rotary_RunId");

            migrationBuilder.RenameIndex(
                name: "IX_Rotaries_ParentId",
                table: "Rotary",
                newName: "IX_Rotary_ParentId");

            migrationBuilder.RenameIndex(
                name: "IX_ReferencedJobs_ReferencedTenantId_ReferencedJobId",
                table: "ReferencedJob",
                newName: "IX_ReferencedJob_ReferencedTenantId_ReferencedJobId");

            migrationBuilder.RenameIndex(
                name: "IX_ReferencedJobs_JobId",
                table: "ReferencedJob",
                newName: "IX_ReferencedJob_JobId");

            migrationBuilder.RenameIndex(
                name: "IX_Passives_RunId_Order",
                table: "Passive",
                newName: "IX_Passive_RunId_Order");

            migrationBuilder.RenameIndex(
                name: "IX_Passives_RunId",
                table: "Passive",
                newName: "IX_Passive_RunId");

            migrationBuilder.RenameIndex(
                name: "IX_PassiveFiles_PassiveId",
                table: "PassiveFile",
                newName: "IX_PassiveFile_PassiveId");

            migrationBuilder.RenameIndex(
                name: "IX_Operators_Name",
                table: "Operator",
                newName: "IX_Operator_Name");

            migrationBuilder.RenameIndex(
                name: "IX_Logs_LoggingId_Depth",
                table: "Log",
                newName: "IX_Log_LoggingId_Depth");

            migrationBuilder.RenameIndex(
                name: "IX_Logs_LoggingId",
                table: "Log",
                newName: "IX_Log_LoggingId");

            migrationBuilder.RenameIndex(
                name: "IX_Loggings_RotaryRunId",
                table: "Logging",
                newName: "IX_Logging_RotaryRunId");

            migrationBuilder.RenameIndex(
                name: "IX_Loggings_PassiveRunId",
                table: "Logging",
                newName: "IX_Logging_PassiveRunId");

            migrationBuilder.RenameIndex(
                name: "IX_Loggings_MagneticId",
                table: "Logging",
                newName: "IX_Logging_MagneticId");

            migrationBuilder.RenameIndex(
                name: "IX_Loggings_LogSettingId",
                table: "Logging",
                newName: "IX_Logging_LogSettingId");

            migrationBuilder.RenameIndex(
                name: "IX_Loggings_GradientRunId",
                table: "Logging",
                newName: "IX_Logging_GradientRunId");

            migrationBuilder.RenameIndex(
                name: "IX_Loggings_CalibrationId",
                table: "Logging",
                newName: "IX_Logging_CalibrationId");

            migrationBuilder.RenameIndex(
                name: "IX_LoggingFiles_LoggingId",
                table: "LoggingFile",
                newName: "IX_LoggingFile_LoggingId");

            migrationBuilder.RenameIndex(
                name: "IX_JobUsers_UserId",
                table: "JobUser",
                newName: "IX_JobUser_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_GyroShots_ShotId",
                table: "GyroShot",
                newName: "IX_GyroShot_ShotId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientSolutions_GradientId",
                table: "GradientSolution",
                newName: "IX_GradientSolution_GradientId");

            migrationBuilder.RenameIndex(
                name: "IX_Gradients_RunId_Order",
                table: "Gradient",
                newName: "IX_Gradient_RunId_Order");

            migrationBuilder.RenameIndex(
                name: "IX_Gradients_RunId",
                table: "Gradient",
                newName: "IX_Gradient_RunId");

            migrationBuilder.RenameIndex(
                name: "IX_Gradients_ParentId",
                table: "Gradient",
                newName: "IX_Gradient_ParentId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientModels_TargetWellId",
                table: "GradientModel",
                newName: "IX_GradientModel_TargetWellId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientModels_InjectionWellId",
                table: "GradientModel",
                newName: "IX_GradientModel_InjectionWellId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientFiles_GradientId",
                table: "GradientFile",
                newName: "IX_GradientFile_GradientId");

            migrationBuilder.RenameIndex(
                name: "IX_Formations_WellId",
                table: "Formation",
                newName: "IX_Formation_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_CommonMeasures_WellId",
                table: "CommonMeasure",
                newName: "IX_CommonMeasure_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_Calibrations_Name_CalibrationString",
                table: "Calibration",
                newName: "IX_Calibration_Name_CalibrationString");

            migrationBuilder.RenameIndex(
                name: "IX_ActiveFields_ShotId",
                table: "ActiveField",
                newName: "IX_ActiveField_ShotId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Well",
                table: "Well",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tubular",
                table: "Tubular",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ToolSurvey",
                table: "ToolSurvey",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TieOn",
                table: "TieOn",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Survey",
                table: "Survey",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Shot",
                table: "Shot",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SavedGradientModel",
                table: "SavedGradientModel",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Run",
                table: "Run",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RotarySolution",
                table: "RotarySolution",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RotaryModel",
                table: "RotaryModel",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RotaryFile",
                table: "RotaryFile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Rotary",
                table: "Rotary",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReferencedJob",
                table: "ReferencedJob",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Passive",
                table: "Passive",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PassiveFile",
                table: "PassiveFile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Operator",
                table: "Operator",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Log",
                table: "Log",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LoggingSetting",
                table: "LoggingSetting",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Logging",
                table: "Logging",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LoggingFile",
                table: "LoggingFile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_JobUser",
                table: "JobUser",
                columns: new[] { "JobId", "UserId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Job",
                table: "Job",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GyroShot",
                table: "GyroShot",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GradientSolution",
                table: "GradientSolution",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Gradient",
                table: "Gradient",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GradientModel",
                table: "GradientModel",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GradientFile",
                table: "GradientFile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Formation",
                table: "Formation",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CommonMeasure",
                table: "CommonMeasure",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Comment",
                table: "Comment",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Calibration",
                table: "Calibration",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ActiveField",
                table: "ActiveField",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ActiveField_Shot_ShotId",
                table: "ActiveField",
                column: "ShotId",
                principalTable: "Shot",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CommonMeasure_Well_WellId",
                table: "CommonMeasure",
                column: "WellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Formation_Well_WellId",
                table: "Formation",
                column: "WellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Gradient_Gradient_ParentId",
                table: "Gradient",
                column: "ParentId",
                principalTable: "Gradient",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Gradient_Run_RunId",
                table: "Gradient",
                column: "RunId",
                principalTable: "Run",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientComment_Comment_CommentsId",
                table: "GradientComment",
                column: "CommentsId",
                principalTable: "Comment",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientComment_Gradient_GradientsId",
                table: "GradientComment",
                column: "GradientsId",
                principalTable: "Gradient",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientFile_Gradient_GradientId",
                table: "GradientFile",
                column: "GradientId",
                principalTable: "Gradient",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModel_Well_InjectionWellId",
                table: "GradientModel",
                column: "InjectionWellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModel_Well_TargetWellId",
                table: "GradientModel",
                column: "TargetWellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModelRun_GradientModel_GradientModelsId",
                table: "GradientModelRun",
                column: "GradientModelsId",
                principalTable: "GradientModel",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModelRun_Run_RunsId",
                table: "GradientModelRun",
                column: "RunsId",
                principalTable: "Run",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientSolution_Gradient_GradientId",
                table: "GradientSolution",
                column: "GradientId",
                principalTable: "Gradient",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GyroShot_Shot_ShotId",
                table: "GyroShot",
                column: "ShotId",
                principalTable: "Shot",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_JobUser_Job_JobId",
                table: "JobUser",
                column: "JobId",
                principalTable: "Job",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Log_Logging_LoggingId",
                table: "Log",
                column: "LoggingId",
                principalTable: "Logging",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Logging_Calibration_CalibrationId",
                table: "Logging",
                column: "CalibrationId",
                principalTable: "Calibration",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Logging_LoggingSetting_LogSettingId",
                table: "Logging",
                column: "LogSettingId",
                principalTable: "LoggingSetting",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Logging_Magnetics_MagneticId",
                table: "Logging",
                column: "MagneticId",
                principalTable: "Magnetics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Logging_Run_GradientRunId",
                table: "Logging",
                column: "GradientRunId",
                principalTable: "Run",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Logging_Run_PassiveRunId",
                table: "Logging",
                column: "PassiveRunId",
                principalTable: "Run",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Logging_Run_RotaryRunId",
                table: "Logging",
                column: "RotaryRunId",
                principalTable: "Run",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LoggingEfd_Logging_LoggingId",
                table: "LoggingEfd",
                column: "LoggingId",
                principalTable: "Logging",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LoggingFile_Logging_LoggingId",
                table: "LoggingFile",
                column: "LoggingId",
                principalTable: "Logging",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LoggingProcessing_Logging_LoggingId",
                table: "LoggingProcessing",
                column: "LoggingId",
                principalTable: "Logging",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LoggingTimeDepth_Logging_LoggingId",
                table: "LoggingTimeDepth",
                column: "LoggingId",
                principalTable: "Logging",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Passive_Run_RunId",
                table: "Passive",
                column: "RunId",
                principalTable: "Run",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PassiveComment_Comment_CommentsId",
                table: "PassiveComment",
                column: "CommentsId",
                principalTable: "Comment",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PassiveComment_Passive_PassivesId",
                table: "PassiveComment",
                column: "PassivesId",
                principalTable: "Passive",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PassiveFile_Passive_PassiveId",
                table: "PassiveFile",
                column: "PassiveId",
                principalTable: "Passive",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PassiveLoggingProcessing_Logging_LoggingId",
                table: "PassiveLoggingProcessing",
                column: "LoggingId",
                principalTable: "Logging",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReferencedJob_Job_JobId",
                table: "ReferencedJob",
                column: "JobId",
                principalTable: "Job",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Rotary_Rotary_ParentId",
                table: "Rotary",
                column: "ParentId",
                principalTable: "Rotary",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Rotary_Run_RunId",
                table: "Rotary",
                column: "RunId",
                principalTable: "Run",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryComment_Comment_CommentsId",
                table: "RotaryComment",
                column: "CommentsId",
                principalTable: "Comment",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryComment_Rotary_RotariesId",
                table: "RotaryComment",
                column: "RotariesId",
                principalTable: "Rotary",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryFile_Rotary_RotaryId",
                table: "RotaryFile",
                column: "RotaryId",
                principalTable: "Rotary",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModel_Well_InjectionWellId",
                table: "RotaryModel",
                column: "InjectionWellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModel_Well_TargetWellId",
                table: "RotaryModel",
                column: "TargetWellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModelRun_RotaryModel_RotaryModelsId",
                table: "RotaryModelRun",
                column: "RotaryModelsId",
                principalTable: "RotaryModel",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModelRun_Run_RunsId",
                table: "RotaryModelRun",
                column: "RunsId",
                principalTable: "Run",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryProcessing_Logging_LoggingId",
                table: "RotaryProcessing",
                column: "LoggingId",
                principalTable: "Logging",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotarySolution_Rotary_RotaryId",
                table: "RotarySolution",
                column: "RotaryId",
                principalTable: "Rotary",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Run_Job_JobId",
                table: "Run",
                column: "JobId",
                principalTable: "Job",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RunOperator_Operator_OperatorsId",
                table: "RunOperator",
                column: "OperatorsId",
                principalTable: "Operator",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RunOperator_Run_RunsId",
                table: "RunOperator",
                column: "RunsId",
                principalTable: "Run",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SavedGradientModel_GradientModel_GradientModelId",
                table: "SavedGradientModel",
                column: "GradientModelId",
                principalTable: "GradientModel",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Shot_Calibration_CalibrationsId",
                table: "Shot",
                column: "CalibrationsId",
                principalTable: "Calibration",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shot_Gradient_GradientId",
                table: "Shot",
                column: "GradientId",
                principalTable: "Gradient",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shot_Magnetics_MagneticsId",
                table: "Shot",
                column: "MagneticsId",
                principalTable: "Magnetics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shot_Rotary_RotaryId",
                table: "Shot",
                column: "RotaryId",
                principalTable: "Rotary",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Survey_Well_WellId",
                table: "Survey",
                column: "WellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TieOn_Well_WellId",
                table: "TieOn",
                column: "WellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ToolSurvey_Shot_ShotId",
                table: "ToolSurvey",
                column: "ShotId",
                principalTable: "Shot",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Tubular_Well_WellId",
                table: "Tubular",
                column: "WellId",
                principalTable: "Well",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActiveField_Shot_ShotId",
                table: "ActiveField");

            migrationBuilder.DropForeignKey(
                name: "FK_CommonMeasure_Well_WellId",
                table: "CommonMeasure");

            migrationBuilder.DropForeignKey(
                name: "FK_Formation_Well_WellId",
                table: "Formation");

            migrationBuilder.DropForeignKey(
                name: "FK_Gradient_Gradient_ParentId",
                table: "Gradient");

            migrationBuilder.DropForeignKey(
                name: "FK_Gradient_Run_RunId",
                table: "Gradient");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientComment_Comment_CommentsId",
                table: "GradientComment");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientComment_Gradient_GradientsId",
                table: "GradientComment");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientFile_Gradient_GradientId",
                table: "GradientFile");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientModel_Well_InjectionWellId",
                table: "GradientModel");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientModel_Well_TargetWellId",
                table: "GradientModel");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientModelRun_GradientModel_GradientModelsId",
                table: "GradientModelRun");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientModelRun_Run_RunsId",
                table: "GradientModelRun");

            migrationBuilder.DropForeignKey(
                name: "FK_GradientSolution_Gradient_GradientId",
                table: "GradientSolution");

            migrationBuilder.DropForeignKey(
                name: "FK_GyroShot_Shot_ShotId",
                table: "GyroShot");

            migrationBuilder.DropForeignKey(
                name: "FK_JobUser_Job_JobId",
                table: "JobUser");

            migrationBuilder.DropForeignKey(
                name: "FK_Log_Logging_LoggingId",
                table: "Log");

            migrationBuilder.DropForeignKey(
                name: "FK_Logging_Calibration_CalibrationId",
                table: "Logging");

            migrationBuilder.DropForeignKey(
                name: "FK_Logging_LoggingSetting_LogSettingId",
                table: "Logging");

            migrationBuilder.DropForeignKey(
                name: "FK_Logging_Magnetics_MagneticId",
                table: "Logging");

            migrationBuilder.DropForeignKey(
                name: "FK_Logging_Run_GradientRunId",
                table: "Logging");

            migrationBuilder.DropForeignKey(
                name: "FK_Logging_Run_PassiveRunId",
                table: "Logging");

            migrationBuilder.DropForeignKey(
                name: "FK_Logging_Run_RotaryRunId",
                table: "Logging");

            migrationBuilder.DropForeignKey(
                name: "FK_LoggingEfd_Logging_LoggingId",
                table: "LoggingEfd");

            migrationBuilder.DropForeignKey(
                name: "FK_LoggingFile_Logging_LoggingId",
                table: "LoggingFile");

            migrationBuilder.DropForeignKey(
                name: "FK_LoggingProcessing_Logging_LoggingId",
                table: "LoggingProcessing");

            migrationBuilder.DropForeignKey(
                name: "FK_LoggingTimeDepth_Logging_LoggingId",
                table: "LoggingTimeDepth");

            migrationBuilder.DropForeignKey(
                name: "FK_Passive_Run_RunId",
                table: "Passive");

            migrationBuilder.DropForeignKey(
                name: "FK_PassiveComment_Comment_CommentsId",
                table: "PassiveComment");

            migrationBuilder.DropForeignKey(
                name: "FK_PassiveComment_Passive_PassivesId",
                table: "PassiveComment");

            migrationBuilder.DropForeignKey(
                name: "FK_PassiveFile_Passive_PassiveId",
                table: "PassiveFile");

            migrationBuilder.DropForeignKey(
                name: "FK_PassiveLoggingProcessing_Logging_LoggingId",
                table: "PassiveLoggingProcessing");

            migrationBuilder.DropForeignKey(
                name: "FK_ReferencedJob_Job_JobId",
                table: "ReferencedJob");

            migrationBuilder.DropForeignKey(
                name: "FK_Rotary_Rotary_ParentId",
                table: "Rotary");

            migrationBuilder.DropForeignKey(
                name: "FK_Rotary_Run_RunId",
                table: "Rotary");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryComment_Comment_CommentsId",
                table: "RotaryComment");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryComment_Rotary_RotariesId",
                table: "RotaryComment");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryFile_Rotary_RotaryId",
                table: "RotaryFile");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModel_Well_InjectionWellId",
                table: "RotaryModel");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModel_Well_TargetWellId",
                table: "RotaryModel");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModelRun_RotaryModel_RotaryModelsId",
                table: "RotaryModelRun");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryModelRun_Run_RunsId",
                table: "RotaryModelRun");

            migrationBuilder.DropForeignKey(
                name: "FK_RotaryProcessing_Logging_LoggingId",
                table: "RotaryProcessing");

            migrationBuilder.DropForeignKey(
                name: "FK_RotarySolution_Rotary_RotaryId",
                table: "RotarySolution");

            migrationBuilder.DropForeignKey(
                name: "FK_Run_Job_JobId",
                table: "Run");

            migrationBuilder.DropForeignKey(
                name: "FK_RunOperator_Operator_OperatorsId",
                table: "RunOperator");

            migrationBuilder.DropForeignKey(
                name: "FK_RunOperator_Run_RunsId",
                table: "RunOperator");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedGradientModel_GradientModel_GradientModelId",
                table: "SavedGradientModel");

            migrationBuilder.DropForeignKey(
                name: "FK_Shot_Calibration_CalibrationsId",
                table: "Shot");

            migrationBuilder.DropForeignKey(
                name: "FK_Shot_Gradient_GradientId",
                table: "Shot");

            migrationBuilder.DropForeignKey(
                name: "FK_Shot_Magnetics_MagneticsId",
                table: "Shot");

            migrationBuilder.DropForeignKey(
                name: "FK_Shot_Rotary_RotaryId",
                table: "Shot");

            migrationBuilder.DropForeignKey(
                name: "FK_Survey_Well_WellId",
                table: "Survey");

            migrationBuilder.DropForeignKey(
                name: "FK_TieOn_Well_WellId",
                table: "TieOn");

            migrationBuilder.DropForeignKey(
                name: "FK_ToolSurvey_Shot_ShotId",
                table: "ToolSurvey");

            migrationBuilder.DropForeignKey(
                name: "FK_Tubular_Well_WellId",
                table: "Tubular");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Well",
                table: "Well");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tubular",
                table: "Tubular");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ToolSurvey",
                table: "ToolSurvey");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TieOn",
                table: "TieOn");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Survey",
                table: "Survey");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Shot",
                table: "Shot");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SavedGradientModel",
                table: "SavedGradientModel");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Run",
                table: "Run");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RotarySolution",
                table: "RotarySolution");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RotaryModel",
                table: "RotaryModel");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RotaryFile",
                table: "RotaryFile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Rotary",
                table: "Rotary");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReferencedJob",
                table: "ReferencedJob");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PassiveFile",
                table: "PassiveFile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Passive",
                table: "Passive");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Operator",
                table: "Operator");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LoggingSetting",
                table: "LoggingSetting");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LoggingFile",
                table: "LoggingFile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Logging",
                table: "Logging");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Log",
                table: "Log");

            migrationBuilder.DropPrimaryKey(
                name: "PK_JobUser",
                table: "JobUser");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Job",
                table: "Job");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GyroShot",
                table: "GyroShot");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GradientSolution",
                table: "GradientSolution");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GradientModel",
                table: "GradientModel");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GradientFile",
                table: "GradientFile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Gradient",
                table: "Gradient");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Formation",
                table: "Formation");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CommonMeasure",
                table: "CommonMeasure");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Comment",
                table: "Comment");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Calibration",
                table: "Calibration");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ActiveField",
                table: "ActiveField");

            migrationBuilder.RenameTable(
                name: "Well",
                newName: "Wells");

            migrationBuilder.RenameTable(
                name: "Tubular",
                newName: "Tubulars");

            migrationBuilder.RenameTable(
                name: "ToolSurvey",
                newName: "ToolSurveys");

            migrationBuilder.RenameTable(
                name: "TieOn",
                newName: "TieOns");

            migrationBuilder.RenameTable(
                name: "Survey",
                newName: "Surveys");

            migrationBuilder.RenameTable(
                name: "Shot",
                newName: "Shots");

            migrationBuilder.RenameTable(
                name: "SavedGradientModel",
                newName: "SavedGradientModels");

            migrationBuilder.RenameTable(
                name: "Run",
                newName: "Runs");

            migrationBuilder.RenameTable(
                name: "RotarySolution",
                newName: "RotarySolutions");

            migrationBuilder.RenameTable(
                name: "RotaryModel",
                newName: "RotaryModels");

            migrationBuilder.RenameTable(
                name: "RotaryFile",
                newName: "RotaryFiles");

            migrationBuilder.RenameTable(
                name: "Rotary",
                newName: "Rotaries");

            migrationBuilder.RenameTable(
                name: "ReferencedJob",
                newName: "ReferencedJobs");

            migrationBuilder.RenameTable(
                name: "PassiveFile",
                newName: "PassiveFiles");

            migrationBuilder.RenameTable(
                name: "Passive",
                newName: "Passives");

            migrationBuilder.RenameTable(
                name: "Operator",
                newName: "Operators");

            migrationBuilder.RenameTable(
                name: "LoggingSetting",
                newName: "LoggingSettings");

            migrationBuilder.RenameTable(
                name: "LoggingFile",
                newName: "LoggingFiles");

            migrationBuilder.RenameTable(
                name: "Logging",
                newName: "Loggings");

            migrationBuilder.RenameTable(
                name: "Log",
                newName: "Logs");

            migrationBuilder.RenameTable(
                name: "JobUser",
                newName: "JobUsers");

            migrationBuilder.RenameTable(
                name: "Job",
                newName: "Jobs");

            migrationBuilder.RenameTable(
                name: "GyroShot",
                newName: "GyroShots");

            migrationBuilder.RenameTable(
                name: "GradientSolution",
                newName: "GradientSolutions");

            migrationBuilder.RenameTable(
                name: "GradientModel",
                newName: "GradientModels");

            migrationBuilder.RenameTable(
                name: "GradientFile",
                newName: "GradientFiles");

            migrationBuilder.RenameTable(
                name: "Gradient",
                newName: "Gradients");

            migrationBuilder.RenameTable(
                name: "Formation",
                newName: "Formations");

            migrationBuilder.RenameTable(
                name: "CommonMeasure",
                newName: "CommonMeasures");

            migrationBuilder.RenameTable(
                name: "Comment",
                newName: "Comments");

            migrationBuilder.RenameTable(
                name: "Calibration",
                newName: "Calibrations");

            migrationBuilder.RenameTable(
                name: "ActiveField",
                newName: "ActiveFields");

            migrationBuilder.RenameIndex(
                name: "IX_Tubular_WellId_Order",
                table: "Tubulars",
                newName: "IX_Tubulars_WellId_Order");

            migrationBuilder.RenameIndex(
                name: "IX_Tubular_WellId",
                table: "Tubulars",
                newName: "IX_Tubulars_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_ToolSurvey_ShotId",
                table: "ToolSurveys",
                newName: "IX_ToolSurveys_ShotId");

            migrationBuilder.RenameIndex(
                name: "IX_TieOn_WellId",
                table: "TieOns",
                newName: "IX_TieOns_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_Survey_WellId_Depth",
                table: "Surveys",
                newName: "IX_Surveys_WellId_Depth");

            migrationBuilder.RenameIndex(
                name: "IX_Survey_WellId",
                table: "Surveys",
                newName: "IX_Surveys_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_Shot_RotaryId",
                table: "Shots",
                newName: "IX_Shots_RotaryId");

            migrationBuilder.RenameIndex(
                name: "IX_Shot_MagneticsId",
                table: "Shots",
                newName: "IX_Shots_MagneticsId");

            migrationBuilder.RenameIndex(
                name: "IX_Shot_GradientId",
                table: "Shots",
                newName: "IX_Shots_GradientId");

            migrationBuilder.RenameIndex(
                name: "IX_Shot_CalibrationsId",
                table: "Shots",
                newName: "IX_Shots_CalibrationsId");

            migrationBuilder.RenameIndex(
                name: "IX_SavedGradientModel_GradientModelId",
                table: "SavedGradientModels",
                newName: "IX_SavedGradientModels_GradientModelId");

            migrationBuilder.RenameIndex(
                name: "IX_SavedGradientModel_CreationTime",
                table: "SavedGradientModels",
                newName: "IX_SavedGradientModels_CreationTime");

            migrationBuilder.RenameIndex(
                name: "IX_Run_Type",
                table: "Runs",
                newName: "IX_Runs_Type");

            migrationBuilder.RenameIndex(
                name: "IX_Run_JobId",
                table: "Runs",
                newName: "IX_Runs_JobId");

            migrationBuilder.RenameIndex(
                name: "IX_RotarySolution_RotaryId",
                table: "RotarySolutions",
                newName: "IX_RotarySolutions_RotaryId");

            migrationBuilder.RenameIndex(
                name: "IX_RotaryModel_TargetWellId",
                table: "RotaryModels",
                newName: "IX_RotaryModels_TargetWellId");

            migrationBuilder.RenameIndex(
                name: "IX_RotaryModel_InjectionWellId",
                table: "RotaryModels",
                newName: "IX_RotaryModels_InjectionWellId");

            migrationBuilder.RenameIndex(
                name: "IX_RotaryFile_RotaryId",
                table: "RotaryFiles",
                newName: "IX_RotaryFiles_RotaryId");

            migrationBuilder.RenameIndex(
                name: "IX_Rotary_RunId_Order",
                table: "Rotaries",
                newName: "IX_Rotaries_RunId_Order");

            migrationBuilder.RenameIndex(
                name: "IX_Rotary_RunId",
                table: "Rotaries",
                newName: "IX_Rotaries_RunId");

            migrationBuilder.RenameIndex(
                name: "IX_Rotary_ParentId",
                table: "Rotaries",
                newName: "IX_Rotaries_ParentId");

            migrationBuilder.RenameIndex(
                name: "IX_ReferencedJob_ReferencedTenantId_ReferencedJobId",
                table: "ReferencedJobs",
                newName: "IX_ReferencedJobs_ReferencedTenantId_ReferencedJobId");

            migrationBuilder.RenameIndex(
                name: "IX_ReferencedJob_JobId",
                table: "ReferencedJobs",
                newName: "IX_ReferencedJobs_JobId");

            migrationBuilder.RenameIndex(
                name: "IX_PassiveFile_PassiveId",
                table: "PassiveFiles",
                newName: "IX_PassiveFiles_PassiveId");

            migrationBuilder.RenameIndex(
                name: "IX_Passive_RunId_Order",
                table: "Passives",
                newName: "IX_Passives_RunId_Order");

            migrationBuilder.RenameIndex(
                name: "IX_Passive_RunId",
                table: "Passives",
                newName: "IX_Passives_RunId");

            migrationBuilder.RenameIndex(
                name: "IX_Operator_Name",
                table: "Operators",
                newName: "IX_Operators_Name");

            migrationBuilder.RenameIndex(
                name: "IX_LoggingFile_LoggingId",
                table: "LoggingFiles",
                newName: "IX_LoggingFiles_LoggingId");

            migrationBuilder.RenameIndex(
                name: "IX_Logging_RotaryRunId",
                table: "Loggings",
                newName: "IX_Loggings_RotaryRunId");

            migrationBuilder.RenameIndex(
                name: "IX_Logging_PassiveRunId",
                table: "Loggings",
                newName: "IX_Loggings_PassiveRunId");

            migrationBuilder.RenameIndex(
                name: "IX_Logging_MagneticId",
                table: "Loggings",
                newName: "IX_Loggings_MagneticId");

            migrationBuilder.RenameIndex(
                name: "IX_Logging_LogSettingId",
                table: "Loggings",
                newName: "IX_Loggings_LogSettingId");

            migrationBuilder.RenameIndex(
                name: "IX_Logging_GradientRunId",
                table: "Loggings",
                newName: "IX_Loggings_GradientRunId");

            migrationBuilder.RenameIndex(
                name: "IX_Logging_CalibrationId",
                table: "Loggings",
                newName: "IX_Loggings_CalibrationId");

            migrationBuilder.RenameIndex(
                name: "IX_Log_LoggingId_Depth",
                table: "Logs",
                newName: "IX_Logs_LoggingId_Depth");

            migrationBuilder.RenameIndex(
                name: "IX_Log_LoggingId",
                table: "Logs",
                newName: "IX_Logs_LoggingId");

            migrationBuilder.RenameIndex(
                name: "IX_JobUser_UserId",
                table: "JobUsers",
                newName: "IX_JobUsers_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_GyroShot_ShotId",
                table: "GyroShots",
                newName: "IX_GyroShots_ShotId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientSolution_GradientId",
                table: "GradientSolutions",
                newName: "IX_GradientSolutions_GradientId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientModel_TargetWellId",
                table: "GradientModels",
                newName: "IX_GradientModels_TargetWellId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientModel_InjectionWellId",
                table: "GradientModels",
                newName: "IX_GradientModels_InjectionWellId");

            migrationBuilder.RenameIndex(
                name: "IX_GradientFile_GradientId",
                table: "GradientFiles",
                newName: "IX_GradientFiles_GradientId");

            migrationBuilder.RenameIndex(
                name: "IX_Gradient_RunId_Order",
                table: "Gradients",
                newName: "IX_Gradients_RunId_Order");

            migrationBuilder.RenameIndex(
                name: "IX_Gradient_RunId",
                table: "Gradients",
                newName: "IX_Gradients_RunId");

            migrationBuilder.RenameIndex(
                name: "IX_Gradient_ParentId",
                table: "Gradients",
                newName: "IX_Gradients_ParentId");

            migrationBuilder.RenameIndex(
                name: "IX_Formation_WellId",
                table: "Formations",
                newName: "IX_Formations_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_CommonMeasure_WellId",
                table: "CommonMeasures",
                newName: "IX_CommonMeasures_WellId");

            migrationBuilder.RenameIndex(
                name: "IX_Calibration_Name_CalibrationString",
                table: "Calibrations",
                newName: "IX_Calibrations_Name_CalibrationString");

            migrationBuilder.RenameIndex(
                name: "IX_ActiveField_ShotId",
                table: "ActiveFields",
                newName: "IX_ActiveFields_ShotId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Wells",
                table: "Wells",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tubulars",
                table: "Tubulars",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ToolSurveys",
                table: "ToolSurveys",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TieOns",
                table: "TieOns",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Surveys",
                table: "Surveys",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Shots",
                table: "Shots",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SavedGradientModels",
                table: "SavedGradientModels",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Runs",
                table: "Runs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RotarySolutions",
                table: "RotarySolutions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RotaryModels",
                table: "RotaryModels",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RotaryFiles",
                table: "RotaryFiles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Rotaries",
                table: "Rotaries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReferencedJobs",
                table: "ReferencedJobs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PassiveFiles",
                table: "PassiveFiles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Passives",
                table: "Passives",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Operators",
                table: "Operators",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LoggingSettings",
                table: "LoggingSettings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LoggingFiles",
                table: "LoggingFiles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Loggings",
                table: "Loggings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Logs",
                table: "Logs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_JobUsers",
                table: "JobUsers",
                columns: new[] { "JobId", "UserId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Jobs",
                table: "Jobs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GyroShots",
                table: "GyroShots",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GradientSolutions",
                table: "GradientSolutions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GradientModels",
                table: "GradientModels",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GradientFiles",
                table: "GradientFiles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Gradients",
                table: "Gradients",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Formations",
                table: "Formations",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CommonMeasures",
                table: "CommonMeasures",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Comments",
                table: "Comments",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Calibrations",
                table: "Calibrations",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ActiveFields",
                table: "ActiveFields",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ActiveFields_Shots_ShotId",
                table: "ActiveFields",
                column: "ShotId",
                principalTable: "Shots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CommonMeasures_Wells_WellId",
                table: "CommonMeasures",
                column: "WellId",
                principalTable: "Wells",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Formations_Wells_WellId",
                table: "Formations",
                column: "WellId",
                principalTable: "Wells",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientComment_Comments_CommentsId",
                table: "GradientComment",
                column: "CommentsId",
                principalTable: "Comments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientComment_Gradients_GradientsId",
                table: "GradientComment",
                column: "GradientsId",
                principalTable: "Gradients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientFiles_Gradients_GradientId",
                table: "GradientFiles",
                column: "GradientId",
                principalTable: "Gradients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModelRun_GradientModels_GradientModelsId",
                table: "GradientModelRun",
                column: "GradientModelsId",
                principalTable: "GradientModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModelRun_Runs_RunsId",
                table: "GradientModelRun",
                column: "RunsId",
                principalTable: "Runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModels_Wells_InjectionWellId",
                table: "GradientModels",
                column: "InjectionWellId",
                principalTable: "Wells",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientModels_Wells_TargetWellId",
                table: "GradientModels",
                column: "TargetWellId",
                principalTable: "Wells",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Gradients_Gradients_ParentId",
                table: "Gradients",
                column: "ParentId",
                principalTable: "Gradients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Gradients_Runs_RunId",
                table: "Gradients",
                column: "RunId",
                principalTable: "Runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GradientSolutions_Gradients_GradientId",
                table: "GradientSolutions",
                column: "GradientId",
                principalTable: "Gradients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GyroShots_Shots_ShotId",
                table: "GyroShots",
                column: "ShotId",
                principalTable: "Shots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_JobUsers_Jobs_JobId",
                table: "JobUsers",
                column: "JobId",
                principalTable: "Jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LoggingEfd_Loggings_LoggingId",
                table: "LoggingEfd",
                column: "LoggingId",
                principalTable: "Loggings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LoggingFiles_Loggings_LoggingId",
                table: "LoggingFiles",
                column: "LoggingId",
                principalTable: "Loggings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LoggingProcessing_Loggings_LoggingId",
                table: "LoggingProcessing",
                column: "LoggingId",
                principalTable: "Loggings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Loggings_Calibrations_CalibrationId",
                table: "Loggings",
                column: "CalibrationId",
                principalTable: "Calibrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Loggings_LoggingSettings_LogSettingId",
                table: "Loggings",
                column: "LogSettingId",
                principalTable: "LoggingSettings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Loggings_Magnetics_MagneticId",
                table: "Loggings",
                column: "MagneticId",
                principalTable: "Magnetics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Loggings_Runs_GradientRunId",
                table: "Loggings",
                column: "GradientRunId",
                principalTable: "Runs",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Loggings_Runs_PassiveRunId",
                table: "Loggings",
                column: "PassiveRunId",
                principalTable: "Runs",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Loggings_Runs_RotaryRunId",
                table: "Loggings",
                column: "RotaryRunId",
                principalTable: "Runs",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LoggingTimeDepth_Loggings_LoggingId",
                table: "LoggingTimeDepth",
                column: "LoggingId",
                principalTable: "Loggings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Logs_Loggings_LoggingId",
                table: "Logs",
                column: "LoggingId",
                principalTable: "Loggings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PassiveComment_Comments_CommentsId",
                table: "PassiveComment",
                column: "CommentsId",
                principalTable: "Comments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PassiveComment_Passives_PassivesId",
                table: "PassiveComment",
                column: "PassivesId",
                principalTable: "Passives",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PassiveFiles_Passives_PassiveId",
                table: "PassiveFiles",
                column: "PassiveId",
                principalTable: "Passives",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PassiveLoggingProcessing_Loggings_LoggingId",
                table: "PassiveLoggingProcessing",
                column: "LoggingId",
                principalTable: "Loggings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Passives_Runs_RunId",
                table: "Passives",
                column: "RunId",
                principalTable: "Runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReferencedJobs_Jobs_JobId",
                table: "ReferencedJobs",
                column: "JobId",
                principalTable: "Jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Rotaries_Rotaries_ParentId",
                table: "Rotaries",
                column: "ParentId",
                principalTable: "Rotaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Rotaries_Runs_RunId",
                table: "Rotaries",
                column: "RunId",
                principalTable: "Runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryComment_Comments_CommentsId",
                table: "RotaryComment",
                column: "CommentsId",
                principalTable: "Comments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryComment_Rotaries_RotariesId",
                table: "RotaryComment",
                column: "RotariesId",
                principalTable: "Rotaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryFiles_Rotaries_RotaryId",
                table: "RotaryFiles",
                column: "RotaryId",
                principalTable: "Rotaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModelRun_RotaryModels_RotaryModelsId",
                table: "RotaryModelRun",
                column: "RotaryModelsId",
                principalTable: "RotaryModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModelRun_Runs_RunsId",
                table: "RotaryModelRun",
                column: "RunsId",
                principalTable: "Runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModels_Wells_InjectionWellId",
                table: "RotaryModels",
                column: "InjectionWellId",
                principalTable: "Wells",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryModels_Wells_TargetWellId",
                table: "RotaryModels",
                column: "TargetWellId",
                principalTable: "Wells",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RotaryProcessing_Loggings_LoggingId",
                table: "RotaryProcessing",
                column: "LoggingId",
                principalTable: "Loggings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RotarySolutions_Rotaries_RotaryId",
                table: "RotarySolutions",
                column: "RotaryId",
                principalTable: "Rotaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RunOperator_Operators_OperatorsId",
                table: "RunOperator",
                column: "OperatorsId",
                principalTable: "Operators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RunOperator_Runs_RunsId",
                table: "RunOperator",
                column: "RunsId",
                principalTable: "Runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_Jobs_JobId",
                table: "Runs",
                column: "JobId",
                principalTable: "Jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SavedGradientModels_GradientModels_GradientModelId",
                table: "SavedGradientModels",
                column: "GradientModelId",
                principalTable: "GradientModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Shots_Calibrations_CalibrationsId",
                table: "Shots",
                column: "CalibrationsId",
                principalTable: "Calibrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shots_Gradients_GradientId",
                table: "Shots",
                column: "GradientId",
                principalTable: "Gradients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shots_Magnetics_MagneticsId",
                table: "Shots",
                column: "MagneticsId",
                principalTable: "Magnetics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shots_Rotaries_RotaryId",
                table: "Shots",
                column: "RotaryId",
                principalTable: "Rotaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Surveys_Wells_WellId",
                table: "Surveys",
                column: "WellId",
                principalTable: "Wells",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TieOns_Wells_WellId",
                table: "TieOns",
                column: "WellId",
                principalTable: "Wells",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ToolSurveys_Shots_ShotId",
                table: "ToolSurveys",
                column: "ShotId",
                principalTable: "Shots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Tubulars_Wells_WellId",
                table: "Tubulars",
                column: "WellId",
                principalTable: "Wells",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

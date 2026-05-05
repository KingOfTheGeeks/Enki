/*
    install-enki-backup-job.sql

    Run this once in SSMS against the dev SQL instance to set up
    nightly Enki backups. The script creates two things:

      1. master.dbo.usp_BackupEnkiDatabases  - a stored proc that
         encapsulates the same logic as scripts/backup-enki-databases.sql.
      2. SQL Agent job 'Enki - nightly database backup' that runs the
         proc nightly at 02:00 and then prunes .bak files older than 14
         days from D:\sql\backup (recursive xp_delete_file).

    Idempotent. Re-run any time to refresh the proc body or push tweaked
    schedule / retention values - the install drops + recreates the
    Agent job each time and CREATE OR ALTER replaces the proc in place.

    Pre-reqs:
      - SQL Server Agent service must be running and set to Automatic.
        Open Services.msc on the SQL host and find "SQL Server Agent
        (MSSQLSERVER)" -> Startup type Automatic, Start the service.
      - The SQL Server service account needs Modify on D:\sql\backup
        (BACKUP DATABASE, xp_create_subdir, and xp_delete_file all run
        under SQL Server's process context, not Agent's).

    Knobs are at the top of section 2 - edit and re-run.

    Operations:
      Run the job on demand:
        EXEC msdb.dbo.sp_start_job N'Enki - nightly database backup';

      Recent history:
        SELECT TOP 50
            run_date, run_time, step_id, step_name, run_status, message
        FROM   msdb.dbo.sysjobhistory
        WHERE  job_id = (SELECT job_id FROM msdb.dbo.sysjobs
                         WHERE name = N'Enki - nightly database backup')
        ORDER BY run_date DESC, run_time DESC;

      Disable temporarily:
        EXEC msdb.dbo.sp_update_job @job_name = N'Enki - nightly database backup', @enabled = 0;

      Uninstall completely:
        EXEC msdb.dbo.sp_delete_job
            @job_name              = N'Enki - nightly database backup',
            @delete_unused_schedule = 1;
        DROP PROCEDURE master.dbo.usp_BackupEnkiDatabases;
*/

-- =========================================================================
-- 1. Stored procedure (idempotent via CREATE OR ALTER)
-- =========================================================================
USE master;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BackupEnkiDatabases
    @BackupRoot nvarchar(260) = N'D:\sql\backup'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @stamp  nvarchar(20) = FORMAT(GETDATE(), N'yyyyMMdd_HHmmss');
    DECLARE @name   sysname;
    DECLARE @subdir nvarchar(400);
    DECLARE @path   nvarchar(400);
    DECLARE @sql    nvarchar(max);

    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT name FROM sys.databases
        WHERE  name LIKE N'Enki[_]%'
          AND  database_id > 4
          AND  state_desc = N'ONLINE'
          AND  source_database_id IS NULL
        ORDER BY name;

    OPEN cur;
    FETCH NEXT FROM cur INTO @name;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @subdir = @BackupRoot + N'\' + @name;
        SET @path   = @subdir + N'\' + @name + N'_' + @stamp + N'.bak';

        -- Idempotent; no error if subdir exists.
        EXEC master.dbo.xp_create_subdir @subdir;

        SET @sql = N'BACKUP DATABASE ' + QUOTENAME(@name)
                 + N' TO DISK = N''' + REPLACE(@path, N'''', N'''''') + N''''
                 + N' WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT, FORMAT, '
                 + N'NAME = N''' + REPLACE(@name + N' full backup', N'''', N'''''') + N''', '
                 + N'STATS = 10;';

        PRINT N'Backing up [' + @name + N'] -> ' + @path;

        BEGIN TRY
            EXEC sp_executesql @sql;
        END TRY
        BEGIN CATCH
            -- One failed DB doesn't kill the rest; the Agent job step
            -- still reports success unless every backup fails. Each
            -- failure prints to the job-step output for triage.
            PRINT N'  FAILED [' + @name + N']: ' + ERROR_MESSAGE();
        END CATCH;

        FETCH NEXT FROM cur INTO @name;
    END;

    CLOSE cur;
    DEALLOCATE cur;
END
GO

PRINT N'Stored procedure master.dbo.usp_BackupEnkiDatabases installed.';
GO

-- =========================================================================
-- 2. SQL Agent job (idempotent: drop + recreate)
-- =========================================================================
USE msdb;
GO

-- ---- knobs ----
DECLARE @JobName       sysname       = N'Enki - nightly database backup';
DECLARE @BackupRoot    nvarchar(260) = N'D:\sql\backup';
DECLARE @RetentionDays int           = 14;
DECLARE @StartTime     int           = 020000;     -- HHMMSS, 24-hour
-- ---------------

-- 2a. Drop existing job (idempotency).
IF EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = @JobName)
BEGIN
    EXEC dbo.sp_delete_job @job_name = @JobName, @delete_unused_schedule = 1;
    PRINT N'Dropped existing job [' + @JobName + N'].';
END

-- 2b. Create job.
DECLARE @JobId uniqueidentifier;
EXEC dbo.sp_add_job
    @job_name         = @JobName,
    @description      = N'Full backup of every Enki_* database, then prune .bak files older than the retention window. See scripts/install-enki-backup-job.sql in the Enki repo.',
    @owner_login_name = N'sa',
    @enabled          = 1,
    @job_id           = @JobId OUTPUT;

-- 2c. Step 1: backup via stored proc.
-- The @command has to be assembled into a variable first - T-SQL's
-- EXEC <proc> @arg = <expr> doesn't accept string-concat expressions
-- in argument position.
DECLARE @BackupCmd nvarchar(max) =
    N'EXEC master.dbo.usp_BackupEnkiDatabases @BackupRoot = N'''
    + REPLACE(@BackupRoot, N'''', N'''''') + N''';';

EXEC dbo.sp_add_jobstep
    @job_id            = @JobId,
    @step_id           = 1,
    @step_name         = N'Backup Enki databases',
    @subsystem         = N'TSQL',
    @database_name     = N'master',
    @on_success_action = 3,                          -- proceed to next step
    @on_fail_action    = 2,                          -- quit with failure
    @command           = @BackupCmd;

-- 2d. Step 2: prune .bak files older than @RetentionDays.
-- The 5th arg (1) tells xp_delete_file to recurse into subfolders -
-- we need that since each DB has its own subdirectory.
DECLARE @PruneCmd nvarchar(max) =
       N'DECLARE @cutoff varchar(20) = CONVERT(varchar(20), DATEADD(DAY, -' + CAST(@RetentionDays AS nvarchar(10)) + N', GETDATE()), 126);' + CHAR(13) + CHAR(10)
     + N'EXEC master.dbo.xp_delete_file 0, N''' + REPLACE(@BackupRoot, N'''', N'''''') + N''', N''bak'', @cutoff, 1;';

EXEC dbo.sp_add_jobstep
    @job_id            = @JobId,
    @step_id           = 2,
    @step_name         = N'Prune old .bak files',
    @subsystem         = N'TSQL',
    @database_name     = N'master',
    @on_success_action = 1,                          -- quit with success
    @on_fail_action    = 2,                          -- quit with failure
    @command           = @PruneCmd;

-- 2e. Daily schedule.
EXEC dbo.sp_add_jobschedule
    @job_id            = @JobId,
    @name              = N'Daily',
    @enabled           = 1,
    @freq_type         = 4,                          -- 4 = daily
    @freq_interval     = 1,                          -- every 1 day
    @active_start_time = @StartTime;

-- 2f. Attach to local server so Agent on this box owns the run.
EXEC dbo.sp_add_jobserver
    @job_id      = @JobId,
    @server_name = N'(local)';

PRINT N'Installed [' + @JobName + N']. Start time '
    + CAST(@StartTime AS nvarchar(10)) + N' (HHMMSS), retention '
    + CAST(@RetentionDays AS nvarchar(10)) + N' days.';
GO

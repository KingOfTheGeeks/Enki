/*
    Full backup of every Enki_* user database to
    D:\sql\backup\<DBName>\<DBName>_<yyyyMMdd_HHmmss>.bak

    Skips Artemis / Athena / Identity / system DBs / snapshots / non-ONLINE
    states by filtering on the Enki_ name prefix + state_desc + source_database_id.

    Flags:
      COPY_ONLY    keeps backup out of the differential / log chain
      COMPRESSION  cuts .bak size 60-90% on text-heavy schemas
      CHECKSUM     validates each page as written
      INIT, FORMAT overwrite if filename collides (timestamp makes this defensive only)
      STATS = 10   prints "10 percent processed..." progress

    Pre-reqs:
      1. D:\sql\backup must exist on this SQL Server host.
      2. The SQL Server service account needs Modify on D:\sql\backup
         (per-DB subfolders are created automatically via xp_create_subdir).

    A single failed DB doesn't stop the others; errors are caught per
    DB and surfaced via PRINT.
*/
SET NOCOUNT ON;

DECLARE @backupRoot nvarchar(260) = N'D:\sql\backup';
DECLARE @stamp      nvarchar(20)  = FORMAT(GETDATE(), N'yyyyMMdd_HHmmss');

DECLARE @name   sysname;
DECLARE @subdir nvarchar(400);
DECLARE @path   nvarchar(400);
DECLARE @sql    nvarchar(max);

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.databases
    WHERE  name LIKE N'Enki[_]%'        -- only Enki DBs (Enki_Master, Enki_Identity, Enki_<TENANT>_Active/_Archive)
      AND  database_id > 4              -- defensive; system DBs never start with Enki_
      AND  state_desc = N'ONLINE'       -- skip RESTORING / OFFLINE / SUSPECT
      AND  source_database_id IS NULL   -- skip snapshots
    ORDER BY name;

OPEN cur;
FETCH NEXT FROM cur INTO @name;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @subdir = @backupRoot + N'\' + @name;
    SET @path   = @subdir + N'\' + @name + N'_' + @stamp + N'.bak';

    -- Idempotent; no error if the subdir already exists.
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
        PRINT N'  FAILED [' + @name + N']: ' + ERROR_MESSAGE();
    END CATCH;

    FETCH NEXT FROM cur INTO @name;
END;

CLOSE cur;
DEALLOCATE cur;

PRINT N'';
PRINT N'Done.';

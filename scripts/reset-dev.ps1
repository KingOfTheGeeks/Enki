<#
.SYNOPSIS
  Wipes every Enki database on the dev SQL Server so the next host boot
  re-applies migrations and re-seeds from scratch.

.DESCRIPTION
  Drops Enki_Master, Enki_Identity, and every Enki_<code>_Active / _Archive
  database found on the target server. Forces SINGLE_USER with ROLLBACK
  IMMEDIATE first so any live connection (WebApi pool, SSMS) is kicked.

  After this runs:
    - Start the Identity host (applies its fresh Initial; seeds the 12
      SDI users including mike.king as enki-admin).
    - Start the WebApi host (applies master Initial; DevMasterSeeder
      auto-provisions TENANTTEST; DevTenantSeeder drops 1 sample Job
      into its Active DB).
    - Start Blazor, log in, click into TENANTTEST — content is there.

  Stop all three hosts before running this, or SINGLE_USER will kick you
  mid-operation (which still works, just noisier).

.PARAMETER Server
  SQL Server instance. Defaults to the dev rig (10.1.7.50).

.PARAMETER User
  SQL auth login. Defaults to sa (dev rig convention).

.PARAMETER Password
  SQL auth password. No default — pass explicitly or via env var.

.EXAMPLE
  .\scripts\reset-dev.ps1 -Password '!@m@nAdm1n1str@t0r'
#>

param(
    [string] $Server   = '10.1.7.50',
    [string] $User     = 'sa',
    [Parameter(Mandatory=$true)]
    [string] $Password
)

$ErrorActionPreference = 'Stop'

$dropSql = @'
SET NOCOUNT ON;

-- Drop every user DB whose name starts with Enki_ — covers master,
-- identity, and every tenant (Enki_<code>_Active / _Archive). Using
-- sp_executesql per row so a single bad DB doesn't kill the batch.
DECLARE @name sysname;
DECLARE @sql  nvarchar(max);

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.databases
    WHERE name LIKE 'Enki_%'
    ORDER BY name;

OPEN cur;
FETCH NEXT FROM cur INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        SET @sql = N'ALTER DATABASE [' + @name + N'] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;';
        EXEC sp_executesql @sql;

        SET @sql = N'DROP DATABASE [' + @name + N'];';
        EXEC sp_executesql @sql;

        PRINT 'Dropped ' + @name;
    END TRY
    BEGIN CATCH
        PRINT 'FAILED to drop ' + @name + ': ' + ERROR_MESSAGE();
    END CATCH;
    FETCH NEXT FROM cur INTO @name;
END;
CLOSE cur;
DEALLOCATE cur;
'@

Write-Host "Resetting Enki dev databases on $Server..." -ForegroundColor Cyan
sqlcmd -S $Server -U $User -P $Password -d master -Q $dropSql
Write-Host "Done. Start Identity, then WebApi, then Blazor." -ForegroundColor Green

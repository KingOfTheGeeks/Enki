<#
.SYNOPSIS
  Drop every Enki_* database on the staging SQL Server, then re-bootstrap
  with Identity + Master schema, OIDC client, admin user, and the four demo
  tenants. Mirrors the local reset-dev.ps1 + auto-seed flow, but driven
  through the Migrator CLI because Staging skips the IsDevelopment()
  auto-seed path.

.DESCRIPTION
  Step order:
    1. Stop sdiamr_identity / sdiamr_webapi / sdiamr_blazor app pools so
       the SINGLE_USER ALTER doesn't have to forcibly disconnect them
       (this script must run on the same box that hosts IIS, or pass
       -SkipPoolControl and stop them yourself).
    2. Drop every database whose name starts with Enki_ (master, identity,
       and every per-tenant Active/Archive pair).
    3. Run Migrator bootstrap-environment — applies Identity + Master
       migrations, master Tools/Calibrations seed, OpenIddict enki-blazor
       client + enki scope, initial admin user.
    4. Run Migrator seed-demo-tenants — provisions PERMIAN, BAKKEN,
       NORTHSEA, CARNARVON with one sample Job each (matches the dev
       fresh-boot state).
    5. Start the pools again, Identity first.

  The Migrator binary is invoked via the path you pass (-MigratorPath)
  or, when omitted, via dotnet run against the source tree (only useful
  when this script is launched from a dev checkout that can reach
  staging SQL directly).

  Required env vars when the script reaches the Migrator step (set them
  in the same PowerShell session before invoking):
    ConnectionStrings__Master           — staging Master DB
    ConnectionStrings__Identity         — staging Identity DB
    Identity__Seed__BlazorClientSecret  — must match BlazorServer's Identity:ClientSecret
    Identity__Seed__AdminEmail
    Identity__Seed__AdminPassword
    Identity__Seed__BlazorBaseUri       — e.g. https://dev.sdiamr.com/

.PARAMETER SqlServer
  SQL Server host that owns the Enki_* DBs.

.PARAMETER SqlUser
  SQL login with dbcreator (sa works).

.PARAMETER SqlPassword
  Password for the SQL login.

.PARAMETER MigratorPath
  Optional. Absolute path to a published SDI.Enki.Migrator.exe. When
  set, the script runs that binary directly. When omitted, falls back to
  `dotnet run --project src/SDI.Enki.Migrator` against the working tree
  (must be invoked from the repo root in that case).

.PARAMETER SkipPoolControl
  Skip the IIS pool stop/start dance. Use when running from a non-IIS
  host or when you've already stopped the pools yourself.

.EXAMPLE
  # Running on the staging box, with the Migrator already published:
  $env:ConnectionStrings__Master           = 'Server=...;Database=Enki_Master;...'
  $env:ConnectionStrings__Identity         = 'Server=...;Database=Enki_Identity;...'
  $env:Identity__Seed__BlazorClientSecret  = '<client-secret>'
  $env:Identity__Seed__AdminEmail          = 'mike.king@sdi.com'
  $env:Identity__Seed__AdminPassword       = '<admin-pwd>'
  $env:Identity__Seed__BlazorBaseUri       = 'https://dev.sdiamr.com/'
  .\scripts\reset-staging.ps1 `
      -SqlServer 'localhost' -SqlUser sa -SqlPassword '<sql-pwd>' `
      -MigratorPath 'C:\Enki\dev\Migrator\SDI.Enki.Migrator.exe'
#>
param(
    [Parameter(Mandatory=$true)] [string] $SqlServer,
    [Parameter(Mandatory=$true)] [string] $SqlUser,
    [Parameter(Mandatory=$true)] [string] $SqlPassword,
    [string] $MigratorPath,
    [switch] $SkipPoolControl
)

$ErrorActionPreference = 'Stop'

$pools = @('sdiamr_identity', 'sdiamr_webapi', 'sdiamr_blazor')

function Stop-EnkiPools {
    Import-Module WebAdministration -ErrorAction SilentlyContinue
    foreach ($p in $pools) {
        if (Get-Item "IIS:\AppPools\$p" -ErrorAction SilentlyContinue) {
            Write-Host "Stopping $p" -ForegroundColor DarkGray
            Stop-WebAppPool -Name $p -ErrorAction SilentlyContinue
        }
    }
}

function Start-EnkiPools {
    Import-Module WebAdministration -ErrorAction SilentlyContinue
    foreach ($p in $pools) {
        if (Get-Item "IIS:\AppPools\$p" -ErrorAction SilentlyContinue) {
            Write-Host "Starting $p" -ForegroundColor DarkGray
            Start-WebAppPool -Name $p -ErrorAction SilentlyContinue
        }
    }
}

# 1. Stop pools.
if (-not $SkipPoolControl) {
    Write-Host "Stopping IIS app pools..." -ForegroundColor Cyan
    Stop-EnkiPools
}

# 2. Drop Enki_* DBs.
Write-Host "Dropping Enki_* databases on $SqlServer..." -ForegroundColor Cyan
$dropSql = @'
SET NOCOUNT ON;
DECLARE @name sysname;
DECLARE @sql  nvarchar(max);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.databases WHERE name LIKE 'Enki_%' ORDER BY name;
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
sqlcmd -S $SqlServer -U $SqlUser -P $SqlPassword -d master -Q $dropSql
if ($LASTEXITCODE -ne 0) { throw "Drop step failed (sqlcmd exit $LASTEXITCODE)." }

# 3 + 4. Migrator: bootstrap-environment, then seed-demo-tenants.
function Invoke-Migrator {
    param([string] $Verb)
    if ($MigratorPath) {
        if (-not (Test-Path $MigratorPath)) {
            throw "MigratorPath '$MigratorPath' does not exist."
        }
        & $MigratorPath $Verb
    }
    else {
        # Source-tree mode. Run from the repo root.
        dotnet run --project src/SDI.Enki.Migrator/SDI.Enki.Migrator.csproj `
            --configuration Release -- $Verb
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Migrator '$Verb' failed (exit $LASTEXITCODE)."
    }
}

Write-Host "Running Migrator bootstrap-environment..." -ForegroundColor Cyan
Invoke-Migrator -Verb 'bootstrap-environment'

Write-Host "Running Migrator seed-demo-tenants..." -ForegroundColor Cyan
Invoke-Migrator -Verb 'seed-demo-tenants'

# 5. Start pools (Identity first so Blazor's wait-for-upstream is happy).
if (-not $SkipPoolControl) {
    Write-Host "Starting IIS app pools..." -ForegroundColor Cyan
    Start-EnkiPools
}

Write-Host ""
Write-Host "Staging reset complete." -ForegroundColor Green
Write-Host "Sign in with the AdminEmail / AdminPassword you set in the env vars." -ForegroundColor Green

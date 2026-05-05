<#
.SYNOPSIS
  Drop every Enki_* database on the staging SQL Server, then re-bootstrap
  it to match the dev rig exactly: schema + 11-user SDI roster + OIDC
  client + 3 demo tenants (PERMIAN / NORTHSEA / BOREAL) with their sample
  wells. Mirrors the local reset-dev.ps1 + dev-bootstrap flow.

.DESCRIPTION
  Step order:
    1. Stop sdiamr_identity / sdiamr_webapi / sdiamr_blazor app pools so
       the SINGLE_USER ALTER doesn't have to forcibly disconnect them
       (this script must run on the same box that hosts IIS, or pass
       -SkipPoolControl and stop them yourself).
    2. Drop every database whose name starts with Enki_ (master, identity,
       and every per-tenant Active/Archive pair).
    3. Run Migrator dev-bootstrap — applies Identity + Master migrations,
       Tools/Calibrations seed, full SDI roster (mike.king, gavin.helboe,
       dapo.ajayi, ...) + OpenIddict enki-blazor client + enki scope,
       and provisions the demo tenants with their sample data.
    4. Start the pools again, Identity first.

  The Migrator binary is invoked via the path you pass (-MigratorPath)
  or, when omitted, via dotnet run against the source tree (only useful
  when this script is launched from a dev checkout that can reach
  staging SQL directly).

  dev-bootstrap refuses to run outside Development by design — the
  in-binary check exists to stop accidental seeding of well-known dev
  fallback creds into a real DB. To use it on staging deliberately
  while keeping that safety, this script:
    a. requires explicit Identity__Seed__* env vars before continuing
       (so the dev fallbacks never apply), and
    b. flips ASPNETCORE_ENVIRONMENT to 'Development' for the duration
       of the Migrator call only. IIS pools have their own per-pool env
       vars in applicationHost config, unaffected by this shell-scoped
       flip — they continue running as Production / Staging.

  Required env vars when the script reaches the Migrator step (set them
  in the same PowerShell session before invoking):
    ConnectionStrings__Master           — staging Master DB
    ConnectionStrings__Identity         — staging Identity DB
    Identity__Seed__BlazorClientSecret  — must match BlazorServer's Identity:ClientSecret
    Identity__Seed__DefaultUserPassword — password applied to every roster user
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
  $env:Identity__Seed__DefaultUserPassword = '<roster-password>'
  $env:Identity__Seed__BlazorBaseUri       = 'https://dev.sdiamr.com/'
  .\scripts\reset-staging.ps1 `
      -SqlServer 'localhost' -SqlUser sa -SqlPassword '<sql-pwd>' `
      -MigratorPath 'C:\Enki\Migrator\Enki.Migrator.exe'
  # End state: sign in at https://dev.sdiamr.com/ as mike.king /
  # <roster-password>. Other roster users use the same password.
#>
param(
    [Parameter(Mandatory=$true)] [string] $SqlServer,
    [Parameter(Mandatory=$true)] [string] $SqlUser,
    [Parameter(Mandatory=$true)] [string] $SqlPassword,
    [string] $MigratorPath,
    [switch] $SkipPoolControl
)

$ErrorActionPreference = 'Stop'

# Sidecar-layout default: redeploy-staging.ps1 drops the published
# Migrator next to this script under C:\Enki\Migrator\. When the sibling
# is present, use it automatically; otherwise leave $MigratorPath empty
# so Invoke-Migrator falls through to the dotnet-run source-tree path
# (dev-checkout usage). Explicit -MigratorPath always wins.
if ([string]::IsNullOrEmpty($MigratorPath)) {
    $sibling = Join-Path $PSScriptRoot 'Migrator\Enki.Migrator.exe'
    if (Test-Path $sibling) { $MigratorPath = $sibling }
}

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
# Drop via ADO.NET rather than sqlcmd — staging boxes often don't have
# the SQL Server CLI tools installed, but System.Data.SqlClient ships
# with .NET so this works on a bare web host. The connection string is
# assembled here rather than reusing $env:ConnectionStrings__Master so
# operators who pass -SqlServer / -SqlUser / -SqlPassword explicitly
# don't have to also set the env var.
$dropConnString = "Server=$SqlServer;Database=master;User Id=$SqlUser;Password=$SqlPassword;TrustServerCertificate=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection $dropConnString
try {
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText    = $dropSql
    $cmd.CommandTimeout = 120
    $cmd.ExecuteNonQuery() | Out-Null
}
catch {
    throw "Drop step failed: $($_.Exception.Message)"
}
finally {
    $conn.Close()
}

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

# Validate the seed env vars BEFORE flipping ASPNETCORE_ENVIRONMENT —
# without explicit values, IdentitySeedData's dev-fallback path would
# silently seed well-known dev creds (Enki!dev1 / enki-blazor-dev-secret)
# into the staging DB. Refuse rather than ship known-public passwords.
$requiredSeedEnv = @(
    @{ Name = 'Identity__Seed__BlazorClientSecret';  Description = 'OIDC client secret shared with the BlazorServer pool.' },
    @{ Name = 'Identity__Seed__DefaultUserPassword'; Description = 'Password applied to every seeded SDI roster user.' },
    @{ Name = 'Identity__Seed__BlazorBaseUri';       Description = 'Public BlazorServer URL (e.g. https://dev.sdiamr.com/).' }
)
foreach ($entry in $requiredSeedEnv) {
    if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($entry.Name))) {
        throw "Required env var '$($entry.Name)' is not set. $($entry.Description) Refusing to fall back to dev defaults on staging."
    }
}

# `dev-bootstrap` checks IsDevelopment() inside the Migrator binary and
# refuses to run otherwise. Flip the variable just for the child process —
# IIS pool env vars are set per-pool in applicationHost config, untouched
# by this shell-scoped change. The required-env-vars guard above ensures
# IdentitySeedData uses explicit creds rather than dev fallbacks.
#
# Both DOTNET_ENVIRONMENT and ASPNETCORE_ENVIRONMENT need to be flipped:
# the Migrator runs under the Generic Host (Host.CreateApplicationBuilder),
# which reads DOTNET_ENVIRONMENT first and only falls back to
# ASPNETCORE_ENVIRONMENT when the former is unset. On a staging box with
# a machine-level DOTNET_ENVIRONMENT=Production, setting only the ASP.NET
# variable is silently ignored.
$savedAspEnv    = $env:ASPNETCORE_ENVIRONMENT
$savedDotnetEnv = $env:DOTNET_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT     = 'Development'
    Write-Host "Running Migrator dev-bootstrap (full roster + demo tenants)..." -ForegroundColor Cyan
    Invoke-Migrator -Verb 'dev-bootstrap'
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $savedAspEnv
    $env:DOTNET_ENVIRONMENT     = $savedDotnetEnv
}

# 5. Start pools (Identity first so Blazor's wait-for-upstream is happy).
if (-not $SkipPoolControl) {
    Write-Host "Starting IIS app pools..." -ForegroundColor Cyan
    Start-EnkiPools
}

Write-Host ""
Write-Host "Staging reset complete - full SDI roster + 3 demo tenants seeded." -ForegroundColor Green
Write-Host "Sign in at https://dev.sdiamr.com/ as 'mike.king' (or any roster user)" -ForegroundColor Green
Write-Host "with the Identity__Seed__DefaultUserPassword you set." -ForegroundColor Green

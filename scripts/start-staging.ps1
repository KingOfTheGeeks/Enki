<#
.SYNOPSIS
  Restart all three Enki staging hosts (Identity, WebApi, Blazor) via
  their IIS app pools. Optionally reset all Enki_* databases first and
  re-bootstrap the schema + admin user + demo tenants.

.DESCRIPTION
  Default behaviour:
    1. Stops the three Enki app pools (sdiamr_identity, sdiamr_webapi,
       sdiamr_blazor) so subsequent starts don't fight a stuck worker
       process or stale connection pool.
    2. Starts the pools again in dependency order:
         a. sdiamr_identity — must be reachable before WebApi tries to
            validate its OIDC discovery + JWKS.
         b. sdiamr_webapi   — must be reachable before Blazor's
            BearerTokenHandler hits any data endpoint.
         c. sdiamr_blazor   — UI; pull https://dev.sdiamr.com/ up in the
            browser once this is up.
       Sleeps between starts so each upstream is responsive before its
       downstream binds.

  With -Reset: stop pools, drop every Enki_* database, run Migrator
  dev-bootstrap (full SDI roster + OIDC client + 3 demo tenants with
  sample wells), then start the pools. Same end-state as the dev rig's
  start-dev.ps1 -Reset, adapted for the staging IIS topology + remote
  SQL. Internally delegates the drop + bootstrap to reset-staging.ps1
  with -SkipPoolControl so the pool dance is owned by this script alone.

  Hosts no longer self-migrate or self-seed in any environment (see
  docs/plan-migrator-bootstrap.md, SDI-ENG-PLAN-002). The Migrator CLI
  is the canonical bootstrap path; -Reset wraps that into one command
  for staging-side operators.

  IIS host prerequisite — one-time per staging box: run
  scripts/install-iis-websockets.ps1 once. It installs the
  IIS-WebSockets Windows feature and unlocks the per-site webSocket
  config section. Without it, the Blazor pool is up but its SignalR
  circuit falls back to long polling and every interactive page
  hangs for 5–8s.

  This script must run on the staging box (the host that owns the IIS
  app pools), in a PowerShell session that has set the Migrator's
  required env vars before invocation:

    ConnectionStrings__Master           — staging Master DB
    ConnectionStrings__Identity         — staging Identity DB
    Identity__Seed__BlazorClientSecret  — must match the Blazor pool's
                                          Identity:ClientSecret env var
    Identity__Seed__DefaultUserPassword — applied to every roster user
    Identity__Seed__BlazorBaseUri       — e.g. https://dev.sdiamr.com/

  reset-staging.ps1 must live alongside this file (scripts\) so the
  -Reset path can call it.

.PARAMETER Reset
  Drop every Enki_* database, then run Migrator bootstrap-environment
  + seed-demo-tenants before starting the pools. Needs SQL credentials
  for the drop step — either passed explicitly via -SqlServer / -SqlUser
  / -SqlPassword, or parsed from `$env:ConnectionStrings__Master` (the
  env var the Migrator reads anyway). Also needs -MigratorPath when
  running on a staging box without a source tree.

.PARAMETER Stop
  Skip the launch entirely. Stops the three Enki app pools and exits.
  Use when you're done testing and want a one-line cleanup.

.PARAMETER SqlServer
  SQL Server host that owns the Enki_* DBs. Optional when -Reset is
  supplied — falls back to parsing the `ConnectionStrings__Master`
  env var when not passed. Ignored without -Reset.

.PARAMETER SqlUser
  SQL login with dbcreator (sa works). Optional when -Reset is
  supplied — falls back to the User ID in `ConnectionStrings__Master`.
  Ignored without -Reset.

.PARAMETER SqlPassword
  Password for the SQL login. Optional when -Reset is supplied —
  falls back to the Password in `ConnectionStrings__Master`. Ignored
  without -Reset.

.PARAMETER MigratorPath
  Optional. Absolute path to a published SDI.Enki.Migrator.exe on the
  staging box. When set, reset-staging.ps1 invokes that binary
  directly. When omitted, reset-staging.ps1 falls back to
  `dotnet run --project src/SDI.Enki.Migrator` against a working tree
  — only useful if you've cloned the repo to the staging box, which
  is rare for a real staging deploy.

.PARAMETER SkipPoolControl
  Skip the IIS pool stop/start dance. Use when you've already stopped
  the pools yourself, or when running from a non-IIS host (rare).

.EXAMPLE
  .\scripts\start-staging.ps1
  Restart the three pools cleanly. No DB changes.

.EXAMPLE
  .\scripts\start-staging.ps1 -Stop
  Stop the three pools and exit.

.EXAMPLE
  $env:ConnectionStrings__Master           = 'Server=10.1.7.50;Database=Enki_Master;User ID=sa;Password=<pwd>;TrustServerCertificate=true'
  $env:ConnectionStrings__Identity         = 'Server=10.1.7.50;Database=Enki_Identity;User ID=sa;Password=<pwd>;TrustServerCertificate=true'
  $env:Identity__Seed__BlazorClientSecret  = '<client-secret>'
  $env:Identity__Seed__DefaultUserPassword = '<roster-password>'
  $env:Identity__Seed__BlazorBaseUri       = 'https://dev.sdiamr.com/'
  .\start-staging.ps1 -Reset -MigratorPath 'C:\Enki\Migrator\Enki.Migrator.exe'
  Wipe every Enki_* DB, re-bootstrap (schema + OIDC client + 11 SDI
  roster users + PERMIAN/NORTHSEA/BOREAL demo tenants with sample
  wells), then bring the three pools back up in order. SQL auth for
  the drop step is parsed out of ConnectionStrings__Master automatically.
  End state matches a fresh `start-dev.ps1 -Reset` on the local rig:
  sign in as 'mike.king' / <roster-password>.

.EXAMPLE
  .\start-staging.ps1 -Reset `
      -SqlServer '10.1.7.50' -SqlUser sa -SqlPassword '<sql-pwd>' `
      -MigratorPath 'C:\Enki\Migrator\Enki.Migrator.exe'
  Same as above, but with SQL creds passed explicitly (use this if
  the Master connection string uses Integrated Security).
#>
param(
    [switch] $Reset,
    [switch] $Stop,
    [string] $SqlServer,
    [string] $SqlUser,
    [string] $SqlPassword,
    [string] $MigratorPath,
    [switch] $SkipPoolControl
)

$ErrorActionPreference = 'Stop'

# Sidecar-layout default: redeploy-staging.ps1 drops the published
# Migrator next to this script under C:\Enki\Migrator\. When the sibling
# is present, use it automatically so the operator can invoke
# `start-staging.ps1 -Reset` with no -MigratorPath arg. Explicit
# -MigratorPath always wins.
if ([string]::IsNullOrEmpty($MigratorPath)) {
    $sibling = Join-Path $PSScriptRoot 'Migrator\Enki.Migrator.exe'
    if (Test-Path $sibling) { $MigratorPath = $sibling }
}

# Pool names match the IIS configuration deployed by redeploy-staging.ps1.
# If you rename a pool in IIS, update this list to match.
$pools = @('sdiamr_identity', 'sdiamr_webapi', 'sdiamr_blazor')

# Helper: WebAdministration's IIS:\AppPools provider only resolves on
# Windows hosts that have the IIS feature installed. Get-Item on a
# non-existent pool throws unless we swallow it explicitly.
function Test-EnkiPool {
    param([string] $Name)
    return $null -ne (Get-Item "IIS:\AppPools\$Name" -ErrorAction SilentlyContinue)
}

function Stop-EnkiPools {
    Import-Module WebAdministration -ErrorAction SilentlyContinue
    Write-Host 'Stopping Enki staging app pools...' -ForegroundColor Yellow
    $found = $false
    foreach ($p in $pools) {
        if (Test-EnkiPool $p) {
            Write-Host "  stopping $p" -ForegroundColor DarkGray
            # Stop-WebAppPool throws a terminating InvalidOperationException
            # ("Object on target path is already stopped") when the pool is
            # already in the Stopped state — -ErrorAction SilentlyContinue
            # doesn't suppress terminating exceptions, so wrap in try/catch.
            # Already-stopped is a no-op for our purposes.
            try { Stop-WebAppPool -Name $p } catch { }
            $found = $true
        }
    }
    if (-not $found) {
        Write-Host '  (no Enki pools found - has staging been deployed?)' -ForegroundColor Gray
    }
}

function Start-EnkiPool {
    param([string] $Name)
    Import-Module WebAdministration -ErrorAction SilentlyContinue
    if (Test-EnkiPool $Name) {
        Write-Host "Starting $Name..." -ForegroundColor Cyan
        # Symmetrical: Start-WebAppPool throws when the pool is already
        # Started. Treat as no-op.
        try { Start-WebAppPool -Name $Name } catch { }
    }
    else {
        throw "Pool '$Name' not found. Has staging been deployed via redeploy-staging.ps1?"
    }
}

# Parse Server / User ID / Password out of the ConnectionStrings__Master
# env var so -Reset doesn't need them passed twice (once here for the
# sqlcmd drop, once in the env var for the Migrator). Returns $null
# if the env var isn't set or doesn't carry SQL auth (Integrated
# Security can't be passed to sqlcmd via -U/-P, so we fall back to
# requiring explicit params in that case).
#
# Uses SqlConnectionStringBuilder so we tolerate the various key
# spellings SQL Server accepts (Server / Data Source / Address;
# User ID / UID; Password / PWD).
function Get-SqlAuthFromEnv {
    $cs = $env:ConnectionStrings__Master
    if ([string]::IsNullOrEmpty($cs)) { return $null }

    Add-Type -AssemblyName System.Data | Out-Null
    try {
        $b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($cs)
    }
    catch {
        Write-Host "  (couldn't parse ConnectionStrings__Master: $($_.Exception.Message))" -ForegroundColor DarkYellow
        return $null
    }

    if ($b.IntegratedSecurity) {
        Write-Host '  (ConnectionStrings__Master uses Integrated Security; pass -SqlServer / -SqlUser / -SqlPassword explicitly for the sqlcmd drop)' -ForegroundColor DarkYellow
        return $null
    }

    return @{
        Server   = $b.DataSource
        User     = $b.UserID
        Password = $b.Password
    }
}

# ---------- handle -Stop and exit ----------
if ($Stop) {
    Stop-EnkiPools
    return
}

# ---------- guard: -Reset needs SQL creds (param OR env var) ----------
# Explicit params win; otherwise parse from ConnectionStrings__Master
# (which the operator has to set anyway for the Migrator step). Lets
# the caller invoke `start-staging.ps1 -Reset` with no extra args once
# the bootstrap env vars are in place.
if ($Reset) {
    if ([string]::IsNullOrEmpty($SqlServer) -or
        [string]::IsNullOrEmpty($SqlUser) -or
        [string]::IsNullOrEmpty($SqlPassword)) {

        $envAuth = Get-SqlAuthFromEnv
        if ($envAuth) {
            if ([string]::IsNullOrEmpty($SqlServer))   { $SqlServer   = $envAuth.Server }
            if ([string]::IsNullOrEmpty($SqlUser))     { $SqlUser     = $envAuth.User }
            if ([string]::IsNullOrEmpty($SqlPassword)) { $SqlPassword = $envAuth.Password }
        }
    }

    foreach ($needed in 'SqlServer', 'SqlUser', 'SqlPassword') {
        if ([string]::IsNullOrEmpty((Get-Variable -Name $needed -ValueOnly))) {
            throw "-$needed is required when -Reset is supplied (pass it explicitly, or include Server / User ID / Password in `$env:ConnectionStrings__Master)."
        }
    }

    # Pre-check the Migrator BEFORE stopping pools — otherwise a missing
    # MigratorPath leaves staging with pools down and DBs dropped but
    # never re-seeded. reset-staging.ps1 falls back to a `dotnet run`
    # against a source tree at $repo/src/SDI.Enki.Migrator, which only
    # exists on a dev checkout; on a real staging box the operator must
    # pass -MigratorPath to a published .exe.
    if ([string]::IsNullOrEmpty($MigratorPath)) {
        $sourceTreeProj = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\SDI.Enki.Migrator\SDI.Enki.Migrator.csproj'
        if (-not (Test-Path $sourceTreeProj)) {
            throw "-MigratorPath is required when -Reset is supplied on a staging box (no source tree found at '$sourceTreeProj' for the dotnet-run fallback). Pass the absolute path to a published SDI.Enki.Migrator.exe."
        }
    }
    elseif (-not (Test-Path $MigratorPath)) {
        throw "-MigratorPath '$MigratorPath' does not exist."
    }
}

# ---------- 1. Stop pools before resetting / restarting ----------
if (-not $SkipPoolControl) {
    Stop-EnkiPools
    Start-Sleep -Seconds 2
}

# ---------- 2. Reset databases + bootstrap (optional) ----------
# Delegated to reset-staging.ps1 with -SkipPoolControl so the pool
# stop/start dance stays owned by this script alone (otherwise we'd
# stop pools twice and start them twice).
if ($Reset) {
    $resetScript = Join-Path $PSScriptRoot 'reset-staging.ps1'
    if (-not (Test-Path $resetScript)) {
        throw "reset-staging.ps1 not found alongside start-staging.ps1 (expected at '$resetScript')."
    }

    Write-Host 'Running reset-staging.ps1 (drop + bootstrap + seed demo tenants)...' -ForegroundColor Cyan
    $resetArgs = @{
        SqlServer       = $SqlServer
        SqlUser         = $SqlUser
        SqlPassword     = $SqlPassword
        SkipPoolControl = $true
    }
    if ($MigratorPath) { $resetArgs['MigratorPath'] = $MigratorPath }

    & $resetScript @resetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "reset-staging.ps1 failed (exit $LASTEXITCODE)."
    }
}

# ---------- 3. Start pools (Identity first) ----------
if (-not $SkipPoolControl) {
    Start-EnkiPool 'sdiamr_identity'
    Start-Sleep -Seconds 12

    Start-EnkiPool 'sdiamr_webapi'
    Start-Sleep -Seconds 25

    Start-EnkiPool 'sdiamr_blazor'
}

Write-Host ''
Write-Host 'All three staging hosts launching via IIS pools.' -ForegroundColor Green
Write-Host '  Identity: https://dev-shamash.sdiamr.com/' -ForegroundColor Gray
Write-Host '  WebApi:   https://dev-isimud.sdiamr.com/' -ForegroundColor Gray
Write-Host '  Blazor:   https://dev.sdiamr.com/   (open this in your browser)' -ForegroundColor Gray
Write-Host ''
if ($Reset) {
    Write-Host 'Reset was applied — full SDI roster + 3 demo tenants seeded.' -ForegroundColor Yellow
    Write-Host "Sign in as 'mike.king' (or any roster user) with your" -ForegroundColor Yellow
    Write-Host 'Identity__Seed__DefaultUserPassword. Old cookies hold tokens against' -ForegroundColor Yellow
    Write-Host 'the regenerated Identity signing cert and will 401.' -ForegroundColor Yellow
}

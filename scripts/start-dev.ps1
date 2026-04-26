<#
.SYNOPSIS
  Launches all three Enki dev hosts (Identity, WebApi, Blazor) in
  dependency order, each in its own PowerShell window. Optionally
  drops every Enki_* database first so the next boot re-migrates and
  re-seeds from a clean slate.

.DESCRIPTION
  Default behaviour:
    1. Stops any running Enki host processes (file locks block builds
       and port binds collide with new launches).
    2. Builds the whole solution so any entity / seeder change you
       just made lands in the running binaries.
    3. Opens three PowerShell windows in order:
         a. Identity  (port 5196) — applies its own migrations, seeds
            users + the OIDC client / scope.
         b. WebApi    (port 5107) — applies master migrations, runs
            DevMasterSeeder which provisions the four demo tenants
            (PERMIAN, BAKKEN, NORTHSEA, CARNARVON) and runs
            DevTenantSeeder against each Active DB.
         c. Blazor    (port 5073) — UI; pull this URL up in the browser.
       Sleeps between launches so Identity's discovery endpoints are
       reachable before WebApi tries to validate them, and WebApi has
       finished provisioning + seeding before Blazor sends its first
       authed request.

  With -Reset, drops every Enki_* database before building so the
  WebApi boot re-creates Master, re-provisions all four demo
  tenants, and re-runs DevTenantSeeder against each fresh Active DB.
  Use this when you've changed a seeder or a tenant migration and
  want the new shape live without manual cleanup.

.PARAMETER Reset
  Drop every Enki_* database before building. Reuses the same SQL
  drop logic as scripts/reset-dev.ps1.

.PARAMETER SkipBuild
  Skip the upfront `dotnet build` and launch whatever's already in
  the bin folders. Use when you just built and only need to relaunch.

.PARAMETER Stop
  Skip the launch entirely. Kills any running Enki host processes
  AND closes the three spawned PowerShell windows (matched by the
  'Enki: ...' titles set on launch). Use when you're done testing
  and want one command to clean up after `start-dev.ps1`.

.PARAMETER Password
  SQL sa password for the dev rig. Defaults to the convention used
  in reset-dev.ps1's example; override if your dev rig differs.

.PARAMETER Server
  SQL Server instance. Defaults to the dev rig at 10.1.7.50.

.EXAMPLE
  .\scripts\start-dev.ps1 -Reset
  Wipe all Enki_* databases, build, and launch all three hosts. End
  state: four demo tenants — PERMIAN (Permian Crest Energy, Field),
  BAKKEN (Bakken Ridge Petroleum, Field), NORTHSEA (Brent Atlantic
  Drilling, Metric), CARNARVON (Carnarvon Offshore Pty, Metric) —
  each seeded with one demo Job + lead well + 10 surveys + 1 tie-on
  + 3 tubulars + 3 formations + 4 common measures.

.EXAMPLE
  .\scripts\start-dev.ps1
  Build (or skip with -SkipBuild) and launch with whatever's in the
  databases now.

.EXAMPLE
  .\scripts\start-dev.ps1 -SkipBuild
  Relaunch with already-current binaries.

.EXAMPLE
  .\scripts\start-dev.ps1 -Stop
  Stop everything: kill the three host processes and close their
  PowerShell windows.
#>

param(
    [switch] $Reset,
    [switch] $SkipBuild,
    [switch] $Stop,
    [string] $Password = '!@m@nAdm1n1str@t0r',
    [string] $Server   = '10.1.7.50'
)

$ErrorActionPreference = 'Stop'
$root = 'D:\Mike.King\Workshop\Enki'

# Helper: stop running host .exes + close the spawned PowerShell
# windows we tagged on launch (window titles "Enki: Identity" etc.).
# Used both as a pre-launch cleanup and as the body of -Stop.
function Stop-EnkiDev {
    $hosts = Get-Process `
        -Name 'SDI.Enki.Identity', 'SDI.Enki.WebApi', 'SDI.Enki.BlazorServer' `
        -ErrorAction SilentlyContinue
    if ($hosts) {
        Write-Host 'Stopping Enki host processes...' -ForegroundColor Yellow
        $hosts | ForEach-Object {
            Write-Host "  killing $($_.Name) ($($_.Id))" -ForegroundColor DarkGray
        }
        $hosts | Stop-Process -Force
    }

    # Close every PowerShell window whose title we set in
    # Start-EnkiHost. Matches both Windows PowerShell (powershell.exe)
    # and PowerShell 7 (pwsh.exe) so it works regardless of which
    # shell the user launched the script from.
    $shells = Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.ProcessName -in 'powershell', 'pwsh') -and
            $_.MainWindowTitle -like 'Enki: *'
        }
    if ($shells) {
        Write-Host 'Closing Enki host windows...' -ForegroundColor Yellow
        $shells | ForEach-Object {
            Write-Host "  closing '$($_.MainWindowTitle)' ($($_.Id))" -ForegroundColor DarkGray
        }
        $shells | Stop-Process -Force
    }

    if (-not $hosts -and -not $shells) {
        Write-Host 'Nothing running.' -ForegroundColor Gray
    }
}

# ---------- handle -Stop and exit ----------
if ($Stop) {
    Stop-EnkiDev
    return
}

# ---------- 1. Stop already-running hosts before launching new ones ----------
Stop-EnkiDev
Start-Sleep -Seconds 2

# ---------- 2. Reset databases (optional) ----------
if ($Reset) {
    Write-Host "Dropping all Enki_* databases on $Server..." -ForegroundColor Cyan
    & "$root\scripts\reset-dev.ps1" -Server $Server -Password $Password
    if ($LASTEXITCODE -ne 0) {
        throw 'Reset failed. See sqlcmd output above.'
    }
}

# ---------- 3. Build ----------
if (-not $SkipBuild) {
    Write-Host 'Building solution...' -ForegroundColor Cyan
    dotnet build "$root\SDI.Enki.slnx" -c Debug --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed.'
    }
}

# ---------- 4. Launch hosts ----------
function Start-EnkiHost {
    param(
        [string] $Name,
        [string] $Project,
        [string] $Url
    )

    Write-Host "Launching $Name on $Url..." -ForegroundColor Cyan

    # New PowerShell window per host so each has its own log stream.
    # Backtick-escapes keep the outer shell from expanding $Host /
    # $env — the spawned shell evaluates them itself. --no-build
    # avoids re-building inside each window (we built once above).
    Start-Process powershell -ArgumentList @(
        '-NoExit',
        '-Command',
        "`$Host.UI.RawUI.WindowTitle = 'Enki: $Name'; `$env:ASPNETCORE_URLS = '$Url'; dotnet run --project '$Project' --no-build"
    )
}

Start-EnkiHost 'Identity' "$root\src\SDI.Enki.Identity\SDI.Enki.Identity.csproj" 'http://localhost:5196'
Start-Sleep -Seconds 12

Start-EnkiHost 'WebApi' "$root\src\SDI.Enki.WebApi\SDI.Enki.WebApi.csproj" 'http://localhost:5107'
Start-Sleep -Seconds 25

Start-EnkiHost 'Blazor' "$root\src\SDI.Enki.BlazorServer\SDI.Enki.BlazorServer.csproj" 'http://localhost:5073'

Write-Host ''
Write-Host 'All three hosts launching. Watch each window for boot logs.' -ForegroundColor Green
Write-Host '  Identity: http://localhost:5196' -ForegroundColor Gray
Write-Host '  WebApi:   http://localhost:5107' -ForegroundColor Gray
Write-Host '  Blazor:   http://localhost:5073   (open this in your browser)' -ForegroundColor Gray
Write-Host ''
if ($Reset) {
    Write-Host 'Reset was applied — sign in fresh; old cookies hold tokens against' -ForegroundColor Yellow
    Write-Host 'the regenerated Identity signing cert and will 401.' -ForegroundColor Yellow
}

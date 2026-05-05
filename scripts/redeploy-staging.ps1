<#
.SYNOPSIS
  Republish all three Enki hosts (Identity, WebApi, BlazorServer) to staging
  via the existing MSDeploy "Default Settings" publish profiles, then
  sidecar-deploy the Migrator + staging scripts to the same box via SMB.

.DESCRIPTION
  Each host has a Properties\PublishProfiles\Default Settings.pubxml that
  targets its WMSVC endpoint:
    - SDI.Enki.Identity     -> dev-shamash.sdiamr.com / sdiamr_identity
    - SDI.Enki.WebApi       -> dev-isimud.sdiamr.com  / sdiamr_webapi
    - SDI.Enki.BlazorServer -> dev.sdiamr.com         / sdiamr_blazor

  This script runs `dotnet publish` with PublishProfile + DeployOnBuild=true
  for each project, in the right order so that anything currently using
  Identity is bumped before Blazor talks to it again.

  After the IIS sites land, the Migrator (a console app — no IIS pool, so
  it doesn't fit the MSDeploy + pubxml pattern) is published locally and
  copied to the staging box via SMB into <StagingToolsPath>\Migrator\,
  alongside start-staging.ps1 + reset-staging.ps1. That gives the
  staging-side operator one local entry point:
    powershell -ExecutionPolicy Bypass -File C:\Enki\start-staging.ps1 -Reset
  with no -MigratorPath arg required (the start/reset scripts auto-pick
  up the sibling Migrator folder).

  MSDeploy creds:
    The pubxml stores the username (KOTG-DC-WEB\Administrator). The password
    lives in the matching .pubxml.user file (gitignored, written by VS when
    you tick "Save password" in the Publish dialog). If the .user file is
    missing, dotnet will prompt; do the first publish from VS so the creds
    get saved, then this script works headless after.

  SMB sidecar copy:
    Targets \\<StagingHost>\C$\Enki\ — admin share access. The dev box
    needs to be running as a user with admin rights on the staging box
    (same credentials the MSDeploy step uses). On a fresh dev box you may
    need a `net use \\KOTG-DC-WEB\C$ /user:KOTG-DC-WEB\Administrator <pwd>`
    once per session; thereafter it's silent.

.PARAMETER SkipBuild
  Pass to use an already-built output (faster iteration when the binaries
  are fresh and you only want to re-push the deploy step).

.PARAMETER DeployUserName
  WMSVC username to authenticate the MSDeploy push with. Optional —
  defaults to whatever each pubxml stores (typically
  KOTG-DC-WEB\Administrator). Use this when the encrypted password in
  the .pubxml.user file can't be DPAPI-decrypted in the current shell
  context (different user, machine change, etc.) and you want to skip
  the VS-Save-Password round trip.

.PARAMETER DeployPassword
  Plaintext WMSVC password matching -DeployUserName. Override for the
  .pubxml.user file's EncryptedPassword. Pass it on the command line
  for one-off recovery; for repeat use, prefer re-saving via Visual
  Studio (right-click project → Publish → tick "Save password") so
  the encrypted blob lives on disk in DPAPI form rather than your
  shell history.

.PARAMETER StagingHost
  Windows machine name of the staging IIS box. Default 'KOTG-DC-WEB' matches
  the pubxml's MSDeploy username. Override only if the staging topology
  changes.

.PARAMETER StagingToolsPath
  Local path on the staging box where the Migrator + scripts land.
  Default 'C:\Enki' (sibling of inetpub, not under it).

.PARAMETER SkipSidecar
  Skip the Migrator + scripts SMB copy. Use when only the IIS sites need
  refreshing (e.g. a Blazor-only change with no Migrator schema work).

.EXAMPLE
  # From the repo root, on the dev box that has the source + saved creds:
  .\scripts\redeploy-staging.ps1
#>
param(
    [switch] $SkipBuild,
    [string] $DeployUserName,
    [string] $DeployPassword,
    [string] $StagingHost      = 'KOTG-DC-WEB',
    [string] $StagingToolsPath = 'C:\Enki',
    [switch] $SkipSidecar
)

$ErrorActionPreference = 'Stop'

# Repo root, derived from the script's own location so this works
# regardless of caller's CWD. Mirrors start-dev.ps1's same idiom.
$repoRoot = Split-Path -Parent $PSScriptRoot

# Identity first so Blazor's wait-for-upstream finds a healthy OIDC server
# when its own pool recycles. WebApi second; Blazor last.
$projects = @(
    'src\SDI.Enki.Identity\SDI.Enki.Identity.csproj',
    'src\SDI.Enki.WebApi\SDI.Enki.WebApi.csproj',
    'src\SDI.Enki.BlazorServer\SDI.Enki.BlazorServer.csproj'
)

# `dotnet publish` automatically builds unless --no-build is set; the
# explicit flag here is for the SkipBuild branch.
#
# DeployOnBuild=true is intentionally NOT passed: with .NET SDK 10 it
# combines badly with `dotnet publish` (the Publish target gets pulled
# back in via _PublishBuildAlternative, producing a circular target-
# dependency error). The pubxml's <WebPublishMethod>MSDeploy</WebPublishMethod>
# already triggers the deploy when the Publish target runs — the extra
# flag was redundant on .NET 10 and is fatal as of 10.0.203.
$publishArgs = @(
    '--configuration', 'Release',
    '/p:PublishProfile=Default Settings',
    '/p:AllowUntrustedCertificate=true'
)
if ($SkipBuild) { $publishArgs += '--no-build' }

# CLI overrides for the WMSVC creds. These only matter when the operator
# passes them; otherwise the .pubxml's <UserName> + the .pubxml.user's
# <EncryptedPassword> apply as before. Use when DPAPI can't decrypt the
# saved blob (different user / machine) or the saved password is stale.
if (-not [string]::IsNullOrEmpty($DeployUserName)) {
    $publishArgs += "/p:UserName=$DeployUserName"
}
if (-not [string]::IsNullOrEmpty($DeployPassword)) {
    # MSDeploy reads the Password property; supplying it here overrides
    # whatever the .pubxml.user has. Plaintext on the command line is a
    # tradeoff — see the param doc.
    $publishArgs += "/p:Password=$DeployPassword"
}

foreach ($proj in $projects) {
    Write-Host ""
    Write-Host "=== Publishing $proj ===" -ForegroundColor Cyan
    dotnet publish $proj @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $proj (exit $LASTEXITCODE)."
    }
}

# ---------- Sidecar: Migrator + staging scripts ----------------------------
# IIS sites are in. Now stage the Migrator (console app, no pool) and the
# two staging-side scripts so the operator can run reset/start locally on
# the IIS box without manually copying anything across.
if (-not $SkipSidecar) {
    $migratorOut = Join-Path $repoRoot 'publish\Migrator'
    $migratorProj = Join-Path $repoRoot 'src\SDI.Enki.Migrator\SDI.Enki.Migrator.csproj'

    Write-Host ""
    Write-Host "=== Publishing Migrator (local) ===" -ForegroundColor Cyan
    # ErrorOnDuplicatePublishOutputFiles=false because the Migrator's
    # transitive reference to SDI.Enki.Identity drags Identity's appsettings.json
    # into the publish set alongside the Migrator's own — same filename, two
    # sources. Tolerated: the values are dev defaults and the Migrator reads
    # its own appsettings.json by path priority.
    $migratorArgs = @(
        '--configuration', 'Release',
        '-o', $migratorOut,
        '-p:ErrorOnDuplicatePublishOutputFiles=false'
    )
    if ($SkipBuild) { $migratorArgs += '--no-build' }
    dotnet publish $migratorProj @migratorArgs
    if ($LASTEXITCODE -ne 0) { throw "Migrator publish failed (exit $LASTEXITCODE)." }

    $unc = "\\$StagingHost\$($StagingToolsPath -replace ':','$')"
    $uncMigrator = Join-Path $unc 'Migrator'

    Write-Host ""
    Write-Host "=== Sidecar copy to $unc ===" -ForegroundColor Cyan
    if (-not (Test-Path $unc)) {
        New-Item -ItemType Directory -Path $unc -Force | Out-Null
    }
    if (-not (Test-Path $uncMigrator)) {
        New-Item -ItemType Directory -Path $uncMigrator -Force | Out-Null
    }

    # /MIR mirrors so removed dependencies don't accumulate across deploys.
    # /NJH /NJS suppresses summary banners (less noise in the console).
    # /R:1 /W:1 keeps SMB retries snappy if the box momentarily disconnects.
    # robocopy exit codes 0-7 are "success with various levels of file
    # action"; 8+ is failure. See robocopy /? for the exit-code table.
    Write-Host "  -> Migrator/" -ForegroundColor DarkGray
    robocopy $migratorOut $uncMigrator /MIR /NJH /NJS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy Migrator failed (exit $LASTEXITCODE)." }

    $scriptsSource = Join-Path $repoRoot 'scripts'
    foreach ($script in 'start-staging.ps1', 'reset-staging.ps1') {
        Write-Host "  -> $script" -ForegroundColor DarkGray
        Copy-Item -Path (Join-Path $scriptsSource $script) `
                  -Destination $unc -Force
    }

    Write-Host "Sidecar staged at $StagingToolsPath\ on $StagingHost." -ForegroundColor Green
}

Write-Host ""
Write-Host "All three hosts redeployed to staging." -ForegroundColor Green
Write-Host "Visit https://dev.sdiamr.com/ and verify /tenants + /admin/users." -ForegroundColor Green
if (-not $SkipSidecar) {
    Write-Host ""
    Write-Host "On the staging box you can now run:" -ForegroundColor Green
    Write-Host "  powershell -ExecutionPolicy Bypass -File $StagingToolsPath\start-staging.ps1 -Reset" -ForegroundColor Green
    Write-Host "(after setting the Identity__Seed__* + ConnectionStrings__* env vars)" -ForegroundColor Green
}

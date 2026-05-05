<#
.SYNOPSIS
  Republish all three Enki hosts (Identity, WebApi, BlazorServer) to staging
  via the existing MSDeploy "Default Settings" publish profiles.

.DESCRIPTION
  Each host has a Properties\PublishProfiles\Default Settings.pubxml that
  targets its WMSVC endpoint:
    - SDI.Enki.Identity     -> dev-shamash.sdiamr.com / sdiamr_identity
    - SDI.Enki.WebApi       -> dev-isimud.sdiamr.com  / sdiamr_webapi
    - SDI.Enki.BlazorServer -> dev.sdiamr.com         / sdiamr_blazor

  This script runs `dotnet publish` with PublishProfile + DeployOnBuild=true
  for each project, in the right order so that anything currently using
  Identity is bumped before Blazor talks to it again.

  MSDeploy creds:
    The pubxml stores the username (KOTG-DC-WEB\Administrator). The password
    lives in the matching .pubxml.user file (gitignored, written by VS when
    you tick "Save password" in the Publish dialog). If the .user file is
    missing, dotnet will prompt; do the first publish from VS so the creds
    get saved, then this script works headless after.

.PARAMETER SkipBuild
  Pass to use an already-built output (faster iteration when the binaries
  are fresh and you only want to re-push the deploy step).

.EXAMPLE
  # From the repo root, on the dev box that has the source + saved creds:
  .\scripts\redeploy-staging.ps1
#>
param(
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'

# Identity first so Blazor's wait-for-upstream finds a healthy OIDC server
# when its own pool recycles. WebApi second; Blazor last.
$projects = @(
    'src\SDI.Enki.Identity\SDI.Enki.Identity.csproj',
    'src\SDI.Enki.WebApi\SDI.Enki.WebApi.csproj',
    'src\SDI.Enki.BlazorServer\SDI.Enki.BlazorServer.csproj'
)

# `dotnet publish` automatically builds unless --no-build is set; the explicit
# flag here is for the SkipBuild branch.
$publishArgs = @(
    '--configuration', 'Release',
    '/p:PublishProfile=Default Settings',
    '/p:DeployOnBuild=true',
    '/p:AllowUntrustedCertificate=true'
)
if ($SkipBuild) { $publishArgs += '--no-build' }

foreach ($proj in $projects) {
    Write-Host ""
    Write-Host "=== Publishing $proj ===" -ForegroundColor Cyan
    dotnet publish $proj @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $proj (exit $LASTEXITCODE)."
    }
}

Write-Host ""
Write-Host "All three hosts redeployed to staging." -ForegroundColor Green
Write-Host "Visit https://dev.sdiamr.com/ and verify /tenants + /admin/users." -ForegroundColor Green

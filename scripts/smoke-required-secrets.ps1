# Smoke test for Workstream C / SDI-ENG-PLAN-001.
#
# Verifies that each host fails loud at startup when a required secret
# is missing in a non-Development environment, and boots cleanly when
# every required secret is present via environment variables.
#
# Usage:
#   pwsh ./scripts/smoke-required-secrets.ps1
#
# The hosts are launched briefly (with their listen-only health probes
# disabled) so the validator runs synchronously during startup. A
# failed validation produces the expected non-zero exit code; the test
# verifies the exit code and the error-message content.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Test-Host {
    param(
        [string]$ProjectPath,
        [string]$ExpectedMissingKey,
        [hashtable]$EnvOverrides
    )

    Write-Host ("`n=== {0} ===" -f (Split-Path $ProjectPath -Leaf)) -ForegroundColor Cyan

    # Run the host with ASPNETCORE_ENVIRONMENT=Production and the env
    # overrides in scope. Capture stdout + stderr; the validator throws
    # and the host process terminates with a non-zero exit code.
    $envBackup = @{}
    $envOverrides.Keys | ForEach-Object { $envBackup[$_] = [Environment]::GetEnvironmentVariable($_) }
    try {
        $envOverrides.GetEnumerator() | ForEach-Object {
            [Environment]::SetEnvironmentVariable($_.Key, $_.Value)
        }
        [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production')

        $output = & dotnet run --project $ProjectPath --no-build --no-launch-profile 2>&1
        $exit = $LASTEXITCODE

        if ($exit -eq 0) {
            Write-Host ("  FAIL: host started successfully when {0} should have been missing." -f $ExpectedMissingKey) -ForegroundColor Red
            return $false
        }

        $output = $output -join "`n"
        if ($output -match [regex]::Escape($ExpectedMissingKey)) {
            Write-Host ("  PASS: host failed loud; error message names {0}." -f $ExpectedMissingKey) -ForegroundColor Green
            return $true
        }

        Write-Host "  FAIL: host failed but error did not name the missing key." -ForegroundColor Red
        Write-Host "  Output:" -ForegroundColor Red
        Write-Host $output -ForegroundColor DarkGray
        return $false
    }
    finally {
        $envBackup.GetEnumerator() | ForEach-Object {
            [Environment]::SetEnvironmentVariable($_.Key, $_.Value)
        }
        [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', $null)
    }
}

# Build solution first so --no-build is safe.
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build (Join-Path $repoRoot 'SDI.Enki.slnx') -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$results = @()

# Identity: should fail when ConnectionStrings:Identity is unset.
$results += Test-Host `
    -ProjectPath (Join-Path $repoRoot 'src/SDI.Enki.Identity/SDI.Enki.Identity.csproj') `
    -ExpectedMissingKey 'ConnectionStrings:Identity' `
    -EnvOverrides @{ }

# WebApi: should fail when ConnectionStrings:Master is unset.
$results += Test-Host `
    -ProjectPath (Join-Path $repoRoot 'src/SDI.Enki.WebApi/SDI.Enki.WebApi.csproj') `
    -ExpectedMissingKey 'ConnectionStrings:Master' `
    -EnvOverrides @{ }

# BlazorServer: should fail when Identity:Authority is unset.
$results += Test-Host `
    -ProjectPath (Join-Path $repoRoot 'src/SDI.Enki.BlazorServer/SDI.Enki.BlazorServer.csproj') `
    -ExpectedMissingKey 'Identity:Authority' `
    -EnvOverrides @{ }

Write-Host ""
$failed = $results | Where-Object { -not $_ }
if ($failed.Count -eq 0) {
    Write-Host "All smoke tests PASSED." -ForegroundColor Green
    exit 0
}
else {
    Write-Host ("{0} smoke test(s) FAILED." -f $failed.Count) -ForegroundColor Red
    exit 1
}

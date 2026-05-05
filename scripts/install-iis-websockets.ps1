<#
.SYNOPSIS
  One-time IIS host setup for the staging box: install the IIS-WebSockets
  Windows feature and unlock the system.webServer/webSocket configuration
  section so per-site web.configs can opt in.

.DESCRIPTION
  Blazor Server's SignalR circuit prefers a WebSocket transport. Without
  these two host-side prerequisites the in-process AspNetCoreModuleV2
  doesn't expose IHttpUpgradeFeature to ASP.NET Core, so SignalR drops
  WebSockets from /_blazor/negotiate and clients fall back to long
  polling. The user-visible symptom is 5–8s round-trips on every
  interactive page (Tie-on edit, Magnetic edit, audit-tile expand,
  inline grid edits) and the browser console warning:

    "Failed to connect via WebSockets, using the Long Polling fallback
     transport. This may be due to a VPN or proxy blocking the
     connection."

  Two host-side changes, both one-time and idempotent:

    1. Enable the Windows IIS-WebSockets feature with -All. The -All
       flag is required because IIS-WebSockets has a Web-App-Dev parent;
       without -All the install reports success but the feature stays
       Disabled silently. Verified by `Get-WindowsOptionalFeature`
       returning State = Enabled.

    2. Unlock the system.webServer/webSocket configuration section in
       applicationhost.config via appcmd. By default the section is
       locked at the parent level, which causes IIS to reject any
       per-site <webSocket> element on load with a 500 — even when the
       feature is correctly installed. Verified implicitly: after this
       step the BlazorServer's web.config (which carries
       <webSocket enabled="true" />) loads without error on iisreset.

  Re-running this script on an already-set-up host is a no-op.

  Verification after running:
    * Browser DevTools / Network / WS filter on https://dev.sdiamr.com/
      should show /_blazor as 101 Switching Protocols, not the
      long-polling GET/POST cycle pattern.
    * Browser console should not log the long-polling fallback warning.
    * /_blazor/negotiate response body should list "WebSockets" first
      in availableTransports.

.EXAMPLE
  .\scripts\install-iis-websockets.ps1
  Run once per new staging box. Must be Administrator.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Must run as Administrator (Enable-WindowsOptionalFeature + appcmd both need elevation).'
}

# ---------- 1. Windows feature: IIS-WebSockets ----------
# Enable-WindowsOptionalFeature without -All silently no-ops if the
# Web-App-Dev parent isn't already enabled. Always pass -All so DISM
# brings up the parents transactionally.
$feature = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets
if ($feature.State -ne 'Enabled') {
    Write-Host 'Enabling IIS-WebSockets Windows feature (with -All for parents)...' -ForegroundColor Cyan
    $result = Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -All
    if ($result.RestartNeeded) {
        Write-Host '  RestartNeeded = True. Reboot before re-running this script.' -ForegroundColor Yellow
        return
    }
    Write-Host '  installed (no restart needed).' -ForegroundColor Green
}
else {
    Write-Host 'IIS-WebSockets feature already enabled.' -ForegroundColor DarkGray
}

# ---------- 2. Unlock the webSocket config section ----------
# Even with the feature installed, applicationhost.config locks the
# section by default. Per-site web.configs that include <webSocket>
# crash IIS with a 500 until this is unlocked. appcmd is idempotent
# here — re-running on an already-unlocked host returns the same
# success message.
$appcmd = Join-Path $env:SystemRoot 'system32\inetsrv\appcmd.exe'
if (-not (Test-Path $appcmd)) {
    throw "appcmd.exe not found at '$appcmd' — IIS Management Tools aren't installed on this host."
}

Write-Host 'Unlocking system.webServer/webSocket in applicationhost.config...' -ForegroundColor Cyan
& $appcmd unlock config -section:system.webServer/webSocket
if ($LASTEXITCODE -ne 0) {
    throw "appcmd unlock failed (exit $LASTEXITCODE)."
}

# ---------- 3. Sanity report ----------
Write-Host ''
Write-Host 'Host setup complete.' -ForegroundColor Green
Get-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets |
    Select-Object FeatureName, State |
    Format-List
Write-Host 'Per-site web.config can now carry <webSocket enabled="true" />.' -ForegroundColor Gray
Write-Host 'Run iisreset (or recycle the Blazor app pool) so existing workers reload.' -ForegroundColor Gray

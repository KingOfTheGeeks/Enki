# Enki Production Deployment

This guide covers deploying Enki to production. The system has three hosts that
talk to three databases plus a one-shot CLI for tenant provisioning.

> **Initial deployment scope.** This document targets a direct-host deployment
> with each host terminating HTTPS itself — no reverse proxy. If a proxy
> (NGINX, IIS-ARR, AppGW, ALB) is added later, see the
> [Adding a reverse proxy](#appendix-adding-a-reverse-proxy) appendix at the end.

---

## Topology

```
                 ┌──────────────────────────┐
                 │  SDI.Enki.BlazorServer   │  port 7301 (HTTPS)
                 │  (admin/operations UI)   │──┐
                 └──────────────┬───────────┘  │ talks to both
                                │              │
                  cookie + OIDC │   bearer+OIDC│
                                ▼              ▼
            ┌──────────────────────┐   ┌─────────────────────┐
            │  SDI.Enki.Identity   │   │   SDI.Enki.WebApi   │  port 7302 (HTTPS)
            │  (OIDC server)       │   │   (data API)        │
            │  port 7300 (HTTPS)   │   └──────────┬──────────┘
            └──────────┬───────────┘              │
                       │                          │
                       ▼                          ▼
              ┌────────────────┐         ┌─────────────────┐
              │  Enki_Identity │         │  Enki_Master    │
              │  (SQL Server)  │         │  (SQL Server)   │
              └────────────────┘         └────────┬────────┘
                                                  │
                                                  ▼  per-tenant
                                          ┌────────────────────┐
                                          │  Enki_<TENANT>_Active   │
                                          │  Enki_<TENANT>_Archive  │
                                          │  (one pair / tenant)    │
                                          └────────────────────┘
```

| Host | Port (dev) | Database(s) it owns |
|---|---|---|
| `SDI.Enki.Identity` | 5196 / 7300 | `Enki_Identity` (ASP.NET Identity + OpenIddict) |
| `SDI.Enki.WebApi` | 5275 / 7302 | `Enki_Master` + per-tenant pair, accessed via `ITenantDbContextFactory` |
| `SDI.Enki.BlazorServer` | 5073 / 7301 | none — talks to Identity + WebApi over HTTPS |
| `SDI.Enki.Migrator` | n/a (CLI) | applies migrations to any of the above |

---

## Prerequisites

- **.NET 10 runtime** — `Microsoft.AspNetCore.App` 10.0.x on the host machine.
- **SQL Server 2019+** — local instance or remote. Account needs `dbcreator` for
  first-run migrate; thereafter `db_owner` per database is enough.
- **Identity signing certificate** — a PFX containing the cert + private key
  used to sign OIDC tokens. Doesn't need to be public-CA; can be self-signed.
- **Syncfusion Blazor license key** — required by the BlazorServer host
  (community license is fine for non-commercial; commercial needs a paid key).
- **License signing private key** — RSA private key (PEM) used by
  `HeimdallLicenseFileGenerator` to sign customer `.lic` files. Dev keys live
  at `dev-keys/private.pem` — production must use a fresh key kept out of source.

---

## Configuration matrix

Every host reads its config from `appsettings.json` + `appsettings.Production.json`
+ environment variables (last wins). For production-sensitive keys, prefer
**environment variables** or a secret store; the appsettings files should ship
with placeholders.

### Identity host (`SDI.Enki.Identity`)

| Key | Required (Prod) | Notes |
|---|---|---|
| `ConnectionStrings:Identity` | yes | `Server=<host>;Database=Enki_Identity;...` |
| `Identity:SigningCertificate:Path` | yes | Filesystem path to the OIDC token-signing PFX |
| `Identity:SigningCertificate:Password` | only if PFX is password-protected | Read once at startup |
| `Identity:ClientSecret` | yes | Shared secret with the BlazorServer client. Rotate by updating both ends |
| `OpenTelemetry:Otlp:Endpoint` | optional | If set, traces export here instead of the console |
| `AuditRetention:AuthEventLogDays` | optional | Default 90 (high-volume + PII-laden) |
| `AuditRetention:IdentityAuditLogDays` | optional | Default 365 |
| `AuditRetention:RunAtUtcHour` | optional | Default 03 |
| `ASPNETCORE_URLS` | recommended | `https://+:7300` (or whatever bind) |
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` |
| `Serilog:WriteTo` | optional | If unset, falls back to console + 14-day rolling file |

Each `Identity:*` key has an explicit fail-fast guard in `Program.cs` —
missing config aborts startup with a clear message rather than booting in a
broken state.

### WebApi host (`SDI.Enki.WebApi`)

| Key | Required (Prod) | Notes |
|---|---|---|
| `ConnectionStrings:Master` | yes | `Server=<host>;Database=Enki_Master;...` |
| `ConnectionStrings:TenantTemplate` | yes | Connection-string template used by `ITenantDbContextFactory` to assemble per-tenant strings; `{Database}` placeholder gets substituted |
| `Identity:Issuer` | yes | Must exactly match Identity's resolved issuer (e.g. `https://identity.enki.example/`) |
| `Licensing:PrivateKeyPath` | yes | Filesystem path to the RSA private-key PEM. **Do not** point this at `dev-keys/` in production |
| `RateLimit:ExpensiveRequestsPerMinute` | optional | Default 5; tune based on tenant-provisioning workload |
| `OpenTelemetry:Otlp:Endpoint` | optional | If set, traces export here instead of the console |
| `AuditRetention:MasterAuditLogDays` | optional | Default 365 |
| `AuditRetention:TenantAuditLogDays` | optional | Default 730 |
| `AuditRetention:RunAtUtcHour` | optional | Default 03 |
| `ASPNETCORE_URLS` | recommended | `https://+:7302` |
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` |

### BlazorServer host (`SDI.Enki.BlazorServer`)

| Key | Required (Prod) | Notes |
|---|---|---|
| `Identity:Authority` | yes | OIDC discovery URL (e.g. `https://identity.enki.example/`) |
| `Identity:ClientId` | optional | Default `enki-blazor` |
| `Identity:ClientSecret` | yes | Must match the Identity host's `Identity:ClientSecret` |
| `WebApi:BaseAddress` | yes | e.g. `https://api.enki.example/` |
| `Syncfusion:LicenseKey` | yes | Configured-fail-fast in production |
| `ASPNETCORE_URLS` | recommended | `https://+:7301` |
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` |

The BlazorServer host's cookie `Secure` flag is set to `Always` in non-Development
environments; HTTPS is therefore mandatory.

### Environment-variable form

ASP.NET maps colon to double-underscore in env vars:

```
Identity__Authority=https://identity.enki.example/
ConnectionStrings__Master=Server=...;Database=Enki_Master;...
Syncfusion__LicenseKey=<key>
```

---

## Database migrations

Every host's auto-migrate runs **only in Development**. In production, apply
migrations explicitly via the Migrator CLI **before** the host starts.

### Order

```bash
# 1. Identity DB (OpenIddict + AspNet* tables + AuthEventLog + IdentityAuditLog)
$env:EnkiIdentityCs = "Server=...;Database=Enki_Identity;..."
dotnet ef database update --project src/SDI.Enki.Identity

# 2. Master DB (Tenants, Users, Tools, Licenses, MasterAuditLog)
$env:EnkiMasterCs = "Server=...;Database=Enki_Master;..."
dotnet ef database update --project src/SDI.Enki.Infrastructure --context EnkiMasterDbContext

# 3. Per-tenant DBs are provisioned on demand via the WebApi
#    POST /tenants endpoint (gated by CanProvisionTenants — Supervisor+ or admin).
```

The Migrator CLI (`SDI.Enki.Migrator`) is the production-blessed entry point;
it wraps the EF tool calls above and is what your deploy pipeline should run.

---

## Health probes

Every host exposes **anonymous** health endpoints suitable for load-balancer
liveness / readiness checks.

| Path | Returns | Use for |
|---|---|---|
| `/health` | 200 if everything healthy, 503 otherwise | Smoke / monitoring |
| `/health/live` | 200 if process is up | Liveness — restart on red |
| `/health/ready` | 200 if dependencies (DB, etc.) healthy | Readiness — drain on red |

- **Identity** ready check probes `Enki_Identity` connectivity.
- **WebApi** ready check probes `Enki_Master` connectivity.
- **BlazorServer** ready check is self-only — Blazor can serve the login page
  even if upstreams are momentarily degraded; failing-ready on upstream blip
  would cycle the pod for transient outages that don't affect this host.

Per-tenant database connectivity is intentionally **not** part of WebApi's
`/health/ready` — a single tenant's DB being down should not drain WebApi
traffic for the rest of the fleet. Track per-tenant health separately.

---

## HTTPS contract

- All three hosts terminate HTTPS directly. `UseHttpsRedirection()` runs in
  non-Development; HSTS is on for the BlazorServer host.
- Cookie `Secure` flag is `Always` outside Development — HTTP requests can't
  carry the auth cookie.
- The `Identity:Authority` value must match exactly across BlazorServer's OIDC
  client config and WebApi's `Identity:Issuer`. If the Identity host is reachable
  via multiple hostnames, pick one and use it consistently. Mismatches surface
  as 401s on every WebApi call.

---

## Defense-in-depth headers

All three hosts emit:

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
X-XSS-Protection: 0
```

HSTS is added by the BlazorServer host's `UseHsts()` middleware in non-Development.

---

## Operational tasks

### Provisioning a tenant

Once the master DB is migrated, a Supervisor (or `enki-admin`) posts to
`POST /tenants` (via the BlazorServer Tenants admin page). The endpoint
is gated by `CanProvisionTenants`. The WebApi:

1. Creates `Enki_<CODE>_Active` and `Enki_<CODE>_Archive` databases.
2. Applies the tenant-DB migrations to both.
3. Inserts `Tenant` + `TenantDatabase` rows in master.
4. Returns the new tenant's id + active connection string.

### Generating a license

A Supervisor, an admin, or any Office user holding the `Licensing`
capability claim navigates to `/licenses/new` in the Blazor portal
(gated by `CanManageLicensing`). The wizard collects tools,
calibrations, features, and an operator-supplied license key (GUID), then asks
the WebApi to mint a `.lic` file via `HeimdallLicenseFileGenerator` (which
signs with the RSA key at `Licensing:PrivateKeyPath`). The customer downloads
both the `.lic` and a sidecar `key.txt` containing the licensee + GUID.

### Audit retention

Audit tables (`AuditLog` per tenant, `MasterAuditLog`, `IdentityAuditLog`,
`AuthEventLog`) are append-only. Automated daily prune is built in:

- **Identity host** runs `AuditRetentionService` — prunes `AuthEventLog`
  + `IdentityAuditLog`.
- **WebApi host** runs `MasterAuditRetentionService` (master DB) +
  `TenantAuditRetentionService` (fans out across every active tenant
  and prunes per-tenant `AuditLog`).

All four use a one-minute wake cycle and fire once per UTC day at the
configured hour (default 03:00 — typical low-traffic window). Default
retention windows:

| Table | Default days | Rationale |
|---|---|---|
| `AuthEventLog`     | 90  | High volume + PII-laden |
| `IdentityAuditLog` | 365 | Admin actions, lower volume |
| `MasterAuditLog`   | 365 | Cross-tenant ops events |
| per-tenant `AuditLog` | 730 | Drilling ops history holds business value longer |

Tunable via the `AuditRetention` config section. Set any `*Days` to `0`
(or negative) to disable that table's prune — useful when a regulatory
window demands indefinite retention. Failures isolate per tenant on the
fan-out sweep — one bad tenant doesn't stop the rest.

### Password reset

`AdminUsersController.ResetPassword` returns a temporary password in the API
response body for the admin to read off the screen and hand to the user
out-of-band. There is **no** email delivery; document this in your support
runbook. (Adding email is on the post-launch backlog.)

---

## Logging

Each host writes structured logs via Serilog:

- **Console** sink — always on.
- **Rolling file** sink — `logs/<host>-.log` with daily rotation, 14 days
  retention.

Override via `Serilog:WriteTo` in config to redirect to Seq / OTLP / wherever.

The WebApi host enriches every request with an `X-Request-Id` header (echoed
back to clients) and pushes that id into the log scope so structured queries
can stitch a single request across log lines.

---

## Secret staging

In rough order of preference:

1. **Cloud secret store** (Key Vault, Secrets Manager, etc.) — best for shared
   deployments.
2. **Environment variables** — fine for single-machine deployments managed by
   systemd / Windows Services / containers.
3. **`appsettings.Production.json`** — works but file lives on disk in plaintext;
   make sure permissions exclude all but the service account.

Never commit `appsettings.Production.json` to source control.

---

## First-time deploy checklist

- [ ] SQL Server reachable; service account has `dbcreator`
- [ ] Identity signing PFX staged; `Identity:SigningCertificate:Path` set
- [ ] License RSA private-key PEM staged; `Licensing:PrivateKeyPath` set
- [ ] Syncfusion license key set (`Syncfusion:LicenseKey`)
- [ ] OIDC `ClientSecret` set on **both** Identity + BlazorServer (matching values)
- [ ] `Identity:Authority` and `Identity:Issuer` agree across hosts
- [ ] `WebApi:BaseAddress` points at the WebApi's external URL
- [ ] Migrator CLI applied: Identity DB, Master DB
- [ ] All three `ASPNETCORE_ENVIRONMENT=Production`
- [ ] HTTPS bindings up on all three hosts
- [ ] `/health/live` returns 200 on every host
- [ ] `/health/ready` returns 200 on Identity + WebApi
- [ ] Sign in via the Blazor portal; verify token issuance lands in the
      `AuthEventLog` table
- [ ] Provision a test tenant via the admin UI; verify the Active + Archive
      databases come up green

---

## Known gaps

These are documented now so they don't surprise the first ops team to take
this on. None blocks initial deployment.

- **No CSP header.** The Razor login pages + Blazor Server's SignalR bootstrap
  use inline scripts; a useful Content-Security-Policy needs nonces or a
  hash allowlist. Deferred until a WAF / reverse proxy lands.
- **No password-reset email.** Admin reads the temp password off the screen
  and hands it off out-of-band. Email delivery is on the next-iteration
  backlog.
- **Per-tenant DB health probes** are not part of `/health/ready`. Track
  separately.

---

## Appendix: Adding a reverse proxy

If a reverse proxy (NGINX, IIS-ARR, AppGW, ALB) goes in front of any of these
hosts later, three things change:

1. **Wire `UseForwardedHeaders()`** in each host's `Program.cs`, before
   `UseHttpsRedirection()` and `UseAuthentication()`:

   ```csharp
   app.UseForwardedHeaders(new ForwardedHeadersOptions
   {
       ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
   });
   ```

   Without this, the rate limiter buckets every request into one IP, the
   `AuthEventLog` records the proxy IP not the client, and the cookie `Secure`
   detection mis-fires when the proxy terminates HTTPS.

2. **Trust posture.** Add a config-driven allow-list of trusted proxy IPs
   (e.g. `ReverseProxy:KnownProxies`) and populate `KnownProxies` from it.
   Default .NET trusts loopback only, which is rarely the right answer in
   prod.

3. **Proxy config (NGINX example):**

   ```nginx
   location / {
       proxy_pass         https://127.0.0.1:7301;
       proxy_set_header   Host              $host;
       proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
       proxy_set_header   X-Forwarded-Proto $scheme;
   }
   ```

The `Identity:Authority` / `Identity:Issuer` values must point at the
**externally visible** URL, not the upstream pod address.

# Enki

**Enki** is the web portal for AMR's Marduk drilling-software ecosystem.
It replaces both legacy systems — **Athena** (the older per-Job-DB
system) and **Artemis** (the .NET 8 monolith that succeeded Athena) —
with a multi-host .NET 10 solution that separates concerns: computation
(Marduk), persistence + workflow + auth + UI (Enki), and the field-side
desktop tool (Esagila).

> *Where Enlil thunders, Enki whispers — and the world listens.*

---

## What Enki is

A multi-tenant web platform for storing, processing, and visualising
directional-drilling survey data:

- Tenant administrators provision and manage their tenants' data.
- Drilling engineers create Jobs, attach Wells, run Surveys, capture
  Shots and Runs, and review trajectory plots.
- All math (minimum-curvature, anti-collision, calibration processing,
  license signing) delegates to **Marduk** — Enki itself stores no
  computed-from-raw values without invoking Marduk first.

Domain shorthand:

```
Tenant ── Job ── Well ── Surveys / TieOn / Formations / Tubulars / CommonMeasures / Magnetics
                  │
                  └── Runs (Gradient / Rotary / Passive)
                       └── Shots (Gradient / Rotary)
                       └── Logs
```

---

## Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10, C# `latest`, file-scoped namespaces |
| API | ASP.NET Core MVC (vanilla `[ApiController]`), `Asp.Versioning` v1.0 default |
| UI | Blazor Server (cookie auth, SSR + `@rendermode InteractiveServer` mix), Syncfusion Blazor (Grid, Charts) |
| Identity | ASP.NET Identity + OpenIddict (auth-code + PKCE, refresh tokens) |
| Storage | SQL Server, EF Core 10 code-first |
| Logging / telemetry | Serilog (rolling daily files + console) + OpenTelemetry (traces + metrics, console exporter) |
| Math (cross-repo) | [Marduk](../Marduk/Marduk) — referenced by `<ProjectReference>` not NuGet |

**Deliberate non-choices** — see [`docs/ArchDecisions.md`](docs/ArchDecisions.md):
no MediatR, no AutoMapper, no `IRepository<T>`, no Specification, no
FluentValidation, no Moq. Controllers talk to `DbContext` directly;
tests use hand-rolled fakes.

---

## Architecture

### Four runnable hosts

| Project | Purpose |
|---|---|
| `SDI.Enki.Identity` | OpenIddict authority + ASP.NET Identity. Issues JWTs the WebApi validates. Self-contained — references only `SDI.Enki.Shared`. |
| `SDI.Enki.WebApi` | REST resource server. All `/tenants/{tenantCode}/...` and master-registry endpoints. Validates bearer tokens via OpenIddict.Validation. |
| `SDI.Enki.BlazorServer` | Cookie-authenticated Blazor UI. Bridges to WebApi via `BearerTokenHandler` (DelegatingHandler) that lifts the access token off the auth ticket. |
| `SDI.Enki.Migrator` | CLI for tenant provisioning + cross-tenant migration fan-out. `Enki.Migrator provision --code XXX --name "..."`. |

### Three database tiers

1. **Master DB** — one per deployment. Tenants registry, master Users,
   Tools (master fleet), Calibrations (master), SystemSettings, Licenses.
2. **Per-tenant pair** — every tenant gets `Enki_<CODE>_Active` (RW) +
   `Enki_<CODE>_Archive` (`SET READ_ONLY` after migrations). All Jobs /
   Wells / Surveys / Shots / Runs / Logs / Audit for that tenant live in
   Active.
3. **Identity DB** — `Enki_Identity` with AspNet\* + OpenIddict + user
   preferences.

`TenantDbContext` is **not** registered in DI — built per request via
`TenantDbContextFactory` from the `TenantContext` populated by
`TenantRoutingMiddleware` (5-minute `IMemoryCache`, busted on
deactivate / reactivate).

### Authorization model

| Policy | Who satisfies it |
|---|---|
| `EnkiApiScope` | Any signed-in user with the `enki` scope. |
| `CanAccessTenant` | Tenant member (`master.TenantUsers`) **or** `enki-admin`. |
| `CanManageTenantMembers` | Tenant **Admin** role **or** `enki-admin`. |
| `EnkiAdminOnly` | `enki-admin` cross-tenant operator role only. |

`IsEnkiAdmin` is a column on `ApplicationUser`; the `enki-admin` role
claim is materialized at sign-in by `EnkiUserClaimsPrincipalFactory`
and never persisted to `AspNetUserClaims`.

---

## Getting set up

### Prerequisites

- **.NET SDK 10.0.202** (see [`global.json`](global.json) — `latestFeature` roll-forward).
- **SQL Server** local instance (Developer / Express / LocalDB all fine).
- **Marduk repo cloned as a sibling**: `D:/<user>/Workshop/Marduk/Marduk/`. Override path via `MardukRoot` in a per-machine `Directory.Build.props` if needed.
- **Visual Studio 2022 17.12+** *or* `dotnet` CLI alone (the `slnx` solution loads in either).

### First-time setup

```powershell
# Clone Enki + Marduk side-by-side
cd D:/<user>/Workshop
git clone https://github.com/KingOfTheGeeks/Enki.git
git clone https://github.com/<your-org>/Marduk.git

# Set the master connection string for design-time `dotnet ef` tools
# (one-time, per machine)
[Environment]::SetEnvironmentVariable(
    "EnkiMasterCs",
    "Server=localhost;Database=Enki_Master;Integrated Security=true;TrustServerCertificate=true;",
    "User")

# Drop in your dev appsettings (per host) — see appsettings.Development.json
# templates committed in each host project.

# First boot: applies master + identity migrations, seeds 3 demo tenants.
cd Enki
./scripts/start-dev.ps1
```

The dev rig launches three hosts in order:

| Host | URL |
|---|---|
| Identity | <http://localhost:5196> |
| WebApi | <http://localhost:5107> |
| Blazor | <http://localhost:5073> |

Sign in with the seeded users in `IdentitySeedData` (e.g.
`mike.king` / pinned dev password). The 3 demo tenants
(**PERMIAN**, **NORTHSEA**, **BOREAL**) auto-provision on first boot
with full Wells / Surveys / TieOns / Tubulars / Formations /
CommonMeasures / Magnetics + randomised Runs / Shots / Logs.

### Reset the dev environment

Pre-customer policy: dev data is disposable. When schema or seed shape
changes, drop everything and reprovision rather than patching.

```powershell
./scripts/reset-dev.ps1     # drops every Enki database
./scripts/start-dev.ps1     # re-applies migrations + reseeds
```

`start-dev.ps1 -Reset` does both in one command.

### Solution layout

```
src/
├── SDI.Enki.Shared          DTOs, exceptions, paging, units helpers
├── SDI.Enki.Core             Domain entities (Master/* + TenantDb/*) + abstractions
├── SDI.Enki.Infrastructure   EF Core, DbContexts, tenant provisioning,
│                             survey auto-calc adapter, calibration processor,
│                             licensing, seeders, Data/Seed/{Tools,Calibrations,BinaryFiles}
├── SDI.Enki.Identity         OpenIddict + ASP.NET Identity host
├── SDI.Enki.WebApi           REST API host + multitenancy + authz handlers
├── SDI.Enki.BlazorServer     Blazor Server UI + Syncfusion grids
└── SDI.Enki.Migrator         Tenant-provisioning CLI

tests/
├── SDI.Enki.Core.Tests
├── SDI.Enki.Infrastructure.Tests
├── SDI.Enki.WebApi.Tests
└── SDI.Enki.Isolation.Tests  Cross-tenant isolation contract — highest-stakes suite

docs/                          ArchDecisions.md, test-plan.md
dev-keys/                      RSA dev keypair for Heimdall license signing
                              (DEV ONLY — see dev-keys/README.md)
scripts/                       start-dev.ps1, reset-dev.ps1
samples/                       Sample data + reference materials
```

### Common dev commands

```powershell
# Build everything
dotnet build SDI.Enki.slnx

# Run all tests
dotnet test SDI.Enki.slnx

# Add a new EF migration to a DbContext (master / tenant / identity)
dotnet ef migrations add <Name> --context <Context> `
  --project src/SDI.Enki.Infrastructure `
  --startup-project src/SDI.Enki.Infrastructure `
  --output-dir Migrations/<Master|Tenant>

# Provision a tenant from the CLI
dotnet run --project src/SDI.Enki.Migrator -- provision --code ACME --name "Acme Corp"
```

---

## Production deployment notes

The dev rig glosses over several things production needs explicitly. Stage
these before pointing the hosts at customer infrastructure:

### Identity host

- **Real signing + encryption certs.** Default uses `AddDevelopment*Certificate()`; in production, set `Identity:SigningCertificate:Path` (PFX file path) + optional `Identity:SigningCertificate:Password`. Same cert is used for both signing and encryption. The host throws at startup if the path is missing in non-`Development`.
- **HTTPS-only in front of `/connect/*`.** `DisableTransportSecurityRequirement()` is gated on `IsDevelopment()`; production keeps it on so OpenIddict refuses plain-HTTP token requests.
- **Rate limit.** A 10-req/min/IP fixed-window limiter is applied to `AuthorizationController`; behind a reverse proxy, also wire `app.UseForwardedHeaders(...)` so the limiter sees the real client IP.

### WebApi

- Master DB connection string via `ConnectionStrings__Master`.
- `Identity__Issuer` env var must point at the deployed Identity URL.
- Per-tenant DBs are created at provisioning time; the migrator CLI is the prod path (the host's auto-migrate is `IsDevelopment`-gated).

### BlazorServer

- `Identity:Authority`, `Identity:ClientId`, `Identity:ClientSecret` in env / config.
- `WebApi:BaseAddress` env var.
- `Syncfusion:LicenseKey` (or `SYNCFUSION_LICENSEKEY` env var) — production throws at startup if missing.
- Behind a reverse proxy: `app.UseForwardedHeaders(...)` for correct client-IP attribution + cookie `Secure` flag.

### Cross-host

- All hosts run on HTTPS in production; the dev pipeline tolerates HTTP only for `IsDevelopment()`.
- `dev-keys/private.pem` is **dev only** (committed and considered compromised). Set `Licensing:PrivateKeyPath` to a real RSA-2048 PEM in production.

---

## Testing

```powershell
dotnet test SDI.Enki.slnx
```

| Project | Coverage |
|---|---|
| `SDI.Enki.Core.Tests` | Smart-enum guards, lifecycle transitions, DTO shape contracts. |
| `SDI.Enki.Infrastructure.Tests` | Survey auto-calc, calibration processing, licensing crypto byte-compat, tenant provisioning. |
| `SDI.Enki.WebApi.Tests` | Per-controller action tests against InMemory DbContext + hand-rolled fakes. |
| `SDI.Enki.Isolation.Tests` | The cross-tenant data-isolation contract — *highest-stakes* suite. Adding a tenant-aware endpoint requires a parallel test here. |

No mocking framework. No FluentAssertions. Raw xUnit `Assert.*`.

User-facing manual test plan: [`docs/test-plan.md`](docs/test-plan.md).

---

## Pointers

- **[`docs/ArchDecisions.md`](docs/ArchDecisions.md)** — the canonical "why" doc. 10 numbered decisions, each with what was rejected and why the rejected option is more attractive than it looks.
- **[`docs/test-plan.md`](docs/test-plan.md)** — feature-by-feature manual test plan.
- **[`dev-keys/README.md`](dev-keys/README.md)** — dev RSA keypair for license signing.

---

## Sibling repositories

- **Marduk** — computation engine. Cross-repo `<ProjectReference>` from Enki's `.csproj` files; expected at `../Marduk/Marduk/`. See `Directory.Build.props` `<MardukRoot>` for the path.
- **Esagila** — desktop field tool. Reads/writes `.lic` files Enki signs.
- **Nabu** — tool management / licensing assets pipeline.

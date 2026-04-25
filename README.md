# Enki

Enki is the SDI replacement for legacy **Athena** — drilling-survey and
magnetic-ranging data management for oilfield directional drilling.

This repo holds three runtime hosts, a CLI migrator, and the libraries
they share. The `Marduk` solution next door supplies the survey-math
back-end (`AMR.Core.*`); Enki integrates it, never re-implements it.

## Topology

| Host                   | Project                                  | Purpose                                                                                  |
|------------------------|------------------------------------------|------------------------------------------------------------------------------------------|
| Identity (OIDC)        | `src/SDI.Enki.Identity`                  | ASP.NET Identity + OpenIddict server. Issues bearer tokens. Owns `AspNetUsers`.          |
| Web API                | `src/SDI.Enki.WebApi`                    | REST + SignalR surface. Validates bearer tokens. Owns master + per-tenant DbContexts.    |
| Blazor Server          | `src/SDI.Enki.BlazorServer`              | UI. Reads OIDC tokens for the user; calls Identity (`/admin/users/*`, `/me/*`) + WebApi. |
| Migrator (CLI)         | `src/SDI.Enki.Migrator`                  | Out-of-band EF migration runner for prod cutovers.                                       |

Tenants are flat: each gets one master `Tenant` row and a pair of
per-tenant databases (`Enki_<code>_Active` and `Enki_<code>_Archive`).
`TenantRoutingMiddleware` resolves `{tenantCode}` in the URL to a
per-request `TenantContext` cached in `IMemoryCache`.

## Prereqs

- .NET 10 SDK
- SQL Server 2022 (the dev rig at `10.1.7.50` is the default target)
- The sibling `Marduk` repo cloned to `..\Marduk` — Enki references its
  `AMR.Core.*` projects directly via `ProjectReference`, no NuGet feed.

## First-run

1. **Set connection strings.** The hosts read from
   `appsettings.Development.json` (per-host) or environment variables.
   Master DB: `ConnectionStrings__Master`. Identity DB:
   `ConnectionStrings__Identity`. Both default to the dev rig.

2. **Set seed credentials (dev defaults exist):**
   - `Identity:Seed:DefaultUserPassword` — initial password for the
     SDI roster. Falls back to `Enki!dev1` only when
     `ASPNETCORE_ENVIRONMENT=Development`.
   - `Identity:Seed:BlazorClientSecret` — OIDC client secret for the
     Blazor host. Falls back to `enki-blazor-dev-secret` in dev.
   - In any non-Development environment a missing key throws on
     startup; the host won't silently seed well-known credentials.

3. **Boot order:**
   1. Identity (applies migrations + seeds users on dev only)
   2. WebApi (applies master migrations + auto-provisions `TENANTTEST`
      on dev)
   3. Blazor

4. **Sign in.** Use any account from `SDI.Enki.Shared.Seeding.SeedUsers`.
   `mike.king` is the dev `enki-admin`.

## Resetting dev databases

`scripts/reset-dev.ps1` drops every `Enki_%` database on the target SQL
Server. Use it when a migration goes sideways or when a clean slate is
faster than a partial repair.

```powershell
.\scripts\reset-dev.ps1 -Password '<sa password>'
```

Stop all three hosts first; the script forces `SINGLE_USER WITH
ROLLBACK IMMEDIATE` to kick stragglers, but cleaner to be tidy.

After it runs, restart the hosts in the order above — migrations
re-apply and dev seed data lands on the next boot.

## Tests

Solution-wide:

```powershell
dotnet test .\SDI.Enki.slnx
```

Four projects:
- `SDI.Enki.Core.Tests` — pure-domain (lifecycle, units, smart-enum).
- `SDI.Enki.Infrastructure.Tests` — EF behaviours + lookup race.
- `SDI.Enki.WebApi.Tests` — `WebApplicationFactory<Program>` integration
  with bearer auth faked by `TestAuthHandler`.
- `SDI.Enki.Isolation.Tests` — multi-tenant isolation smoke.

## Documentation

- [`docs/ArchDecisions.md`](docs/ArchDecisions.md) — captured trade-offs
  the codebase silently bakes in (no MediatR, controllers-talk-to-DbContext,
  flat tenancy, …). Read this before proposing a major refactor.
- [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md) — manual click-through plan
  for areas without automated coverage.
- [`docs/architecture-review-2026-04-24.md`](docs/architecture-review-2026-04-24.md)
  and [`docs/architecture-review-2026-04-24-phase8.md`](docs/architecture-review-2026-04-24-phase8.md) — rolling review reports.

## Naming

- **Enki** — codename for this app.
- **Athena** — the legacy app this is replacing.
- **Marduk** — sibling repo with the survey-math IP. Project refs only.

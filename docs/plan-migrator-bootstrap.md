---
title: "Enki — Migrator-driven Environment Bootstrap"
subtitle: "Replace host-startup migration + seed with an explicit deploy-time CLI"
author: "SDI · KingOfTheGeeks"
date: "2026-05-02"
---

# Enki — Migrator-driven Environment Bootstrap

| Field | Value |
| --- | --- |
| Document number | SDI-ENG-PLAN-002 |
| Document type | Engineering Plan |
| Version | 1.0 |
| Status | Implemented |
| Effective date | 2026-05-02 |
| Document owner | Mike King |
| Related docs | SDI-ENG-PLAN-001 (Workstream C secrets management), `docs/deploy.md` |

> **Implementation notes (2026-05-02):** built as planned with two
> additions surfaced during the work:
> - A new `dev-bootstrap` Migrator command (sibling to
>   `bootstrap-environment`) covers the dev-rig path — full
>   `SeedUsers` roster + demo tenants, refuses to run outside
>   Development. `start-dev.ps1 -Reset` invokes it.
> - The redirect URIs on the OpenIddict `enki-blazor` client are now
>   environment-driven via `Identity:Seed:BlazorBaseUri` (required
>   outside Development) — without this, the OIDC client would be
>   created with hardcoded localhost redirects against a non-localhost
>   Blazor host. The Dev path uses `IdentityBootstrapper.DevRedirects()`
>   which keeps both the http and https localhost targets.
>
> The integration tests in
> `tests/SDI.Enki.Migrator.Tests/` exercise `IdentityBootstrapper`
> directly against a Testcontainers MsSql instance (the meat of the
> work). The command-level tests cover the pre-DB validation gates
> without needing Docker, so CI without Docker still gets coverage
> on the missing-config path.
>
> **First-staging-deploy lessons (2026-05-03):** four issues
> surfaced operating the new path against a real IIS host;
> all are fixed in head and documented in `docs/deploy.md §
> IIS app pool gotchas`.
>
> 1. `Migrator/Program.cs` registered `AddIdentityCore<>().AddDefaultTokenProviders()`,
>    which pulls `DataProtectorTokenProvider` and transitively
>    `IDataProtectionProvider`. The CLI's `HostApplicationBuilder`
>    doesn't auto-register DataProtection, so DI validation
>    failed at `Build()`. The bootstrapper never issues tokens —
>    `AddDefaultTokenProviders()` was dead weight; removed.
> 2. The Migrator's `appsettings.Development.json` only carried
>    `ConnectionStrings:Master`. With the host-startup migrate
>    paths gone, the Migrator now owns Identity-DB migration too,
>    so it needs its own `ConnectionStrings:Identity` entry.
> 3. `bootstrap-environment` reads `Identity:Seed:AdminEmail`
>    from configuration — and the operator pasting their email
>    via a chat client picked up a `[email](mailto:email)`
>    markdown wrapper that landed in the env var verbatim.
>    The user creation succeeded (Identity's user validator only
>    enforces uniqueness, not format), but sign-in failed
>    because the wrapped form didn't match the typed email.
>    Surgical SQL UPDATE recovered. Worth a future check in
>    `IdentityBootstrapper.BootstrapForProductionAsync` to
>    reject email values that contain `[` `]` `(` `)` characters
>    not legal in an addr-spec.
> 4. `X509CertificateLoader.LoadPkcs12FromFile` defaults to
>    `DefaultKeySet`, which tries to persist the private key
>    into the **running user's profile** key container. IIS
>    app pool virtual accounts have no profile loaded by
>    default, so the persist step fails — and the CryptoAPI
>    returns `FILE_NOT_FOUND` (about the key container, not
>    the PFX). The error message doesn't make that distinction.
>    Identity host now passes
>    `MachineKeySet | EphemeralKeySet` so the private key
>    stays in-memory and the persist path is never taken.

---

# 0. Executive summary

First boot of any non-Development environment (Staging, Production,
customer) lands on empty databases and crashes — the Identity host's
EF migration + OpenIddict + admin-user seed is gated to
`IsDevelopment()`, and so is the WebApi's master-DB migration + Tools/
Calibrations seed. Either we accept Option A (boot once with
`ASPNETCORE_ENVIRONMENT=Development` against staging SQL so the dev
seeder runs `mike.king`/`Enki!dev1` and the OIDC client secret
`enki-blazor-dev-secret`, then flip env to Staging) or we build the
canonical path: a Migrator CLI command that bootstraps any environment
explicitly, with credentials supplied through deploy-time env vars and
no fallbacks.

This plan covers the canonical path. After it lands, the deploy
sequence is: publish three apps → run
`Enki.Migrator bootstrap-environment` once → start IIS app pools.
Hosts only ever read pre-staged data; the Dev-only startup gates are
removed.

| # | Workstream | Effort | Dep |
| --- | --- | --- | --- |
| A | Extract `IdentityBootstrapper` from `IdentitySeedData` | ~4h | — |
| B | New Migrator commands (`migrate-identity`, `migrate-master`, `bootstrap-environment`) | ~3h | A |
| C | Required-secrets validation on Migrator entry | ~1h | B |
| D | Host startup cleanup (drop Dev-only migrate + seed gates) | ~1h | A, B, F |
| E | Preserve dev experience (`start-dev.ps1 -Reset` calls Migrator) | ~2h | B |
| F | Testcontainers integration test for `bootstrap-environment` | ~3h | A, B |
| G | Docs (`deploy.md`, plan cross-references) | ~1h | D |

**Total effort:** ~15 hours, 1.5–2 dev-days for one engineer focused.

\newpage

# 1. Current state

Three startup paths gated behind `app.Environment.IsDevelopment()`:

- `src/SDI.Enki.Identity/Program.cs:402` — `db.Database.MigrateAsync()`
  + `IdentitySeedData.SeedAsync()`. Recovers from "orphan tables"
  state by drop+recreate (Dev-only safety net).
- `src/SDI.Enki.WebApi/Program.cs:369` — `master.Database.MigrateAsync()`
  + `MasterDataSeeder.SeedAsync()` (Tools + Calibrations from JSON).
- `src/SDI.Enki.WebApi/Program.cs:416` —
  `DevMasterSeeder.SeedAsync()` (auto-provisions PERMIAN / BAKKEN /
  NORTHSEA / CARNARVON when `ProvisioningOptions.SeedSampleData` is on,
  itself only set true under `IsDevelopment()`).

`IdentitySeedData.ResolveCredential` already enforces "no dev fallback
outside Development" for the OIDC client secret and the default user
password — but the seed function is never invoked outside Development
because of the gate. The required-secrets validator on the Identity
host (`src/SDI.Enki.Identity/Program.cs:48-62`) currently *prohibits*
`Identity:Seed:DefaultUserPassword` outside Dev, which keeps the
seeder from being repurposed by stuffing env vars at the host.

The Migrator CLI (`SDI.Enki.Migrator`) handles tenant-DB migrations
and tenant provisioning. It does not touch the Identity DB or the
master DB — those have no CLI surface today.

\newpage

# 2. Target state

## 2.1 Deploy sequence

```
publish all three apps
↓
Enki.Migrator bootstrap-environment    (first-time only)
↓
start IIS app pools
```

For subsequent rolling deploys (schema-only changes, no new admin
user), `Enki.Migrator migrate-all` covers it.

## 2.2 Migrator command surface

| Command | Effect | Idempotent |
| --- | --- | --- |
| `bootstrap-environment` | migrate-identity + migrate-master + seed OpenIddict client/scope + create admin user | Yes |
| `migrate-identity` | EF migrations on `ApplicationDbContext` | Yes |
| `migrate-master` | EF migrations on `EnkiMasterDbContext` | Yes |
| `migrate-tenants` | (existing `migrate`, renamed for clarity) tenant DB migrations | Yes |
| `migrate-all` | identity + master + tenants in order | Yes |
| `provision` | (existing) provision a new tenant | Yes (per existing semantics) |

Existing `migrate` keeps working (alias for `migrate-tenants`) so any
existing deploy scripts don't break.

## 2.3 Required env vars for `bootstrap-environment`

| Key | Purpose |
| --- | --- |
| `ConnectionStrings__Master` | Master DB connection |
| `ConnectionStrings__Identity` | Identity DB connection |
| `Identity__Seed__BlazorClientSecret` | OIDC client secret for `enki-blazor`; same value goes on the BlazorServer pool's `Identity__ClientSecret` env var |
| `Identity__Seed__AdminEmail` | Email of the initial admin user |
| `Identity__Seed__AdminPassword` | Initial password for the admin user |

No dev fallback. Missing → command refuses to run (before touching
the database).

## 2.4 Host startup, post-change

Identity host (`src/SDI.Enki.Identity/Program.cs`):
- Drops the `if (IsDevelopment) { MigrateAsync + SeedAsync }` block.
- Required-secrets validator removes `Identity:Seed:BlazorClientSecret`
  from its `required` list (host doesn't read it any more).
- Required-secrets validator removes the
  `Identity:Seed:DefaultUserPassword` `prohibited` rule (key is no
  longer used; rule is dead).

WebApi host (`src/SDI.Enki.WebApi/Program.cs`):
- Drops the `if (IsDevelopment) { MigrateAsync + MasterDataSeeder }`.
- Drops the unconditional `DevMasterSeeder.SeedAsync` call (its work
  moves into a Migrator subcommand or the dev `start-dev.ps1` flow).

\newpage

# 3. Workstream A — Extract `IdentityBootstrapper`

**Goal:** make the seed logic callable from anywhere (Migrator CLI,
unit test, dev startup) without depending on the host's
`IServiceProvider`.

## 3.1 New service

Path: `src/SDI.Enki.Identity/Bootstrap/IdentityBootstrapper.cs`

Constructor takes:

- `UserManager<ApplicationUser>`
- `RoleManager<IdentityRole>` (only if any seed currently uses roles
  through Identity — currently the role claim is derived from a column
  so this may be unneeded; verify during implementation)
- `IOpenIddictApplicationManager`
- `IOpenIddictScopeManager`
- `IConfiguration` (kept; reads `Identity:Seed:*` keys)
- `ILogger<IdentityBootstrapper>`

Public methods:

- `BootstrapForProductionAsync(adminEmail, adminPassword, blazorClientSecret, ct)` — creates one admin user (UserType=Team, EnkiAdmin=true) and the OpenIddict app+scope. No SeedUsers roster.
- `SeedDevRosterAsync(blazorClientSecret, defaultPassword, ct)` — full SeedUsers roster (existing dev behaviour).

Both methods funnel into a private `EnsureOpenIddictAsync` that lifts
the create-only logic out of `IdentitySeedData.SeedOpenIddictAsync`
unchanged (it's already correctly idempotent).

`BootstrapForProductionAsync` user creation reuses the create-or-
reconcile pattern from `IdentitySeedData.SeedAsync`: if a user with
the given email already exists, reconcile their `IsEnkiAdmin` to true
(stamp rotation) but do NOT touch the password. This makes
`bootstrap-environment` safely re-runnable after rotating the admin
password manually.

## 3.2 IdentitySeedData becomes a thin shim

Replace `IdentitySeedData.SeedAsync` body with a one-line call to
`IdentityBootstrapper.SeedDevRosterAsync` (resolves credentials from
config + dev fallback as today). All the existing logic moves into
the bootstrapper; the shim just keeps existing host wiring working.

## 3.3 Files touched

| Path | Change |
| --- | --- |
| `src/SDI.Enki.Identity/Bootstrap/IdentityBootstrapper.cs` | New |
| `src/SDI.Enki.Identity/Data/IdentitySeedData.cs` | Body moves into bootstrapper; class becomes thin shim |
| `src/SDI.Enki.Identity/Program.cs` | Wire bootstrapper into DI alongside existing identity registrations |

\newpage

# 4. Workstream B — Migrator command additions

## 4.1 csproj

`src/SDI.Enki.Migrator/SDI.Enki.Migrator.csproj` adds:

```xml
<ProjectReference Include="..\SDI.Enki.Identity\SDI.Enki.Identity.csproj" />
```

This pulls in OpenIddict + AspNetCore.Identity transitively. Migrator
is a deploy-time CLI; the dependency size is acceptable.

## 4.2 New command files

| File | Command |
| --- | --- |
| `src/SDI.Enki.Migrator/Commands/MigrateIdentityCommand.cs` | `migrate-identity` |
| `src/SDI.Enki.Migrator/Commands/MigrateMasterCommand.cs` | `migrate-master` |
| `src/SDI.Enki.Migrator/Commands/BootstrapEnvironmentCommand.cs` | `bootstrap-environment` |
| `src/SDI.Enki.Migrator/Commands/MigrateAllCommand.cs` | `migrate-all` |

Each command:
- Resolves config via the existing `Host.CreateApplicationBuilder` setup.
- Validates required env vars via `RequiredSecretsValidator` (Workstream C).
- Operates inside an `IServiceScope` so DbContexts dispose cleanly.

## 4.3 Migrator Program.cs DI

Adds:

```csharp
// Identity DB context + AspNetCore.Identity + OpenIddict registration.
// Same shape as Identity/Program.cs but stripped of the OIDC server
// pieces (we only need the data-layer stores).
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
{
    opt.UseSqlServer(identityConn, sql => sql.EnableRetryOnFailure(...));
    opt.UseOpenIddict();
});
builder.Services.AddIdentityCore<ApplicationUser>(...)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddOpenIddict()
    .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>());
builder.Services.AddScoped<IdentityBootstrapper>();
```

`AddIdentityCore` (not `AddIdentity`) keeps the surface minimal —
no auth pipeline, no claims-principal factory; just the user/role
managers needed for create-and-reconcile.

## 4.4 Routing in `Program.cs`

Existing switch becomes:

```csharp
return args[0] switch
{
    "provision"             => await ProvisionCommand.RunAsync(...),
    "migrate-identity"      => await MigrateIdentityCommand.RunAsync(...),
    "migrate-master"        => await MigrateMasterCommand.RunAsync(...),
    "migrate-tenants"
      or "migrate"          => await MigrateCommand.RunAsync(...),    // existing alias
    "migrate-all"           => await MigrateAllCommand.RunAsync(...),
    "bootstrap-environment" => await BootstrapEnvironmentCommand.RunAsync(...),
    _                       => HelpCommand.Unknown(args[0]),
};
```

\newpage

# 5. Workstream C — Required-secrets validation

`RequiredSecretsValidator.Validate` already exists (used by all three
hosts). Migrator commands invoke it with command-specific lists.

| Command | Required keys |
| --- | --- |
| `migrate-identity` | `ConnectionStrings:Identity` |
| `migrate-master` | `ConnectionStrings:Master` |
| `migrate-tenants` | `ConnectionStrings:Master` |
| `migrate-all` | `ConnectionStrings:Identity`, `ConnectionStrings:Master` |
| `bootstrap-environment` | All of the above + `Identity:Seed:BlazorClientSecret`, `Identity:Seed:AdminEmail`, `Identity:Seed:AdminPassword` |

No `prohibited` rules — Migrator is the deploy-time tool, secrets are
expected.

\newpage

# 6. Workstream D — Host startup cleanup

Done after Workstream F (integration test) is green so we have proof
the new path works before tearing out the old one.

## 6.1 Identity host

Remove `src/SDI.Enki.Identity/Program.cs:400-430` (the Dev-gated
migrate + seed scope block). `IdentitySeedData` retained as a shim
because Workstream E may still call it from a dev-only path.

Required-secrets list trimmed:

```csharp
// REMOVE:
new("Identity:Seed:BlazorClientSecret", ...)

// REMOVE the whole prohibited[] (the only entry was DefaultUserPassword)
```

Add a `prohibited` for the new `bootstrap-environment` keys so they
can never be set on the Identity host:

```csharp
prohibited:
[
    new("Identity:Seed:AdminPassword", "...belongs on the Migrator CLI, not the host"),
    new("Identity:Seed:AdminEmail",    "...belongs on the Migrator CLI, not the host"),
],
```

## 6.2 WebApi host

Remove `src/SDI.Enki.WebApi/Program.cs:369-400` (Dev-gated migrate +
MasterDataSeeder).

Remove the unconditional `DevMasterSeeder.SeedAsync(app.Services)`
call at line 416. Move its logic into:

- A new `Enki.Migrator seed-demo-tenants` command (Dev-only convenience), OR
- The existing `start-dev.ps1` script (calls Migrator commands directly).

The second option keeps the Migrator surface focused on canonical deploy
operations. **Recommended.**

\newpage

# 7. Workstream E — Dev experience

`start-dev.ps1 -Reset` currently drops every Enki_* DB and lets the
hosts re-bootstrap on first request. After Workstream D the hosts no
longer self-bootstrap. The script must run Migrator first.

New script flow:

```powershell
# inside start-dev.ps1 -Reset
Drop-EnkiDatabases
dotnet run --project src/SDI.Enki.Migrator -- bootstrap-environment `
    --env Development     # tells Migrator to use dev fallback creds
dotnet run --project src/SDI.Enki.Migrator -- seed-demo-tenants    # if Workstream D goes route A
StartHosts
```

`bootstrap-environment` in Development env reads from
`appsettings.Development.json` and resolves dev fallback creds via the
existing `ResolveCredential` shape. In any other environment, the no-
fallback rule applies.

\newpage

# 8. Workstream F — Integration test

Path: `tests/SDI.Enki.Migrator.Tests/Bootstrap/BootstrapEnvironmentCommandTests.cs`
(new test project; csproj parallels the existing tests/* projects).

Uses Testcontainers `MsSqlContainer` (already a dependency in
`SDI.Enki.WebApi.Tests`).

| Test | Expectation |
| --- | --- |
| `BootstrapEnvironment_OnEmptyDatabases_CreatesSchemaAndSeedRows` | After run: `__EFMigrationsHistory` exists, `OpenIddictApplications` has `enki-blazor`, `AspNetUsers` has the admin row, scope `enki` exists |
| `BootstrapEnvironment_RunTwice_IsIdempotent` | Run twice, assert second run completes without error and DB row counts unchanged from after first run |
| `BootstrapEnvironment_MissingAdminPassword_FailsBeforeMigrating` | Run with no `Identity:Seed:AdminPassword` env var → command exits non-zero, `__EFMigrationsHistory` table does not exist |
| `BootstrapEnvironment_AdminEmailExists_DoesNotResetPassword` | Pre-create the admin user with a different password, run `bootstrap-environment`, sign in with the *original* password — succeeds |

\newpage

# 9. Workstream G — Docs

- `docs/deploy.md` — replace the "first-time deploy" section with the
  Migrator-driven sequence. Cross-link from the secret-staging table.
- `docs/plan-prototype-security.md` (SDI-ENG-PLAN-001) Workstream C —
  add a forward-pointer to this plan as the canonical bootstrap path.
- `start-dev.ps1` header comment — note that bootstrap now flows through
  Migrator.

\newpage

# 10. Risks

- **DI graph balloons in Migrator.** Adding Identity pulls OpenIddict
  + AspNetCore.Identity into a console CLI. Acceptable — Migrator is
  a deploy-time tool, not runtime.
- **Dev shim regression.** If the dev path through `IdentitySeedData`
  is somehow wired into a non-Dev environment, well-known credentials
  could land in real DBs. **Mitigation:** the existing
  `ResolveCredential` `IsDevelopment` gate stays in place; dev fallback
  values still throw outside Dev.
- **Forgotten admin password.** Bootstrap creates one admin; if the
  initial password is lost before first sign-in, the only recovery is
  manual SQL or a re-bootstrap with a different email. **Mitigation:**
  deploy-doc step explicitly says "verify sign-in works before closing
  the deploy session."
- **OpenIddict client secret rotation.** `EnsureOpenIddictAsync` is
  create-only by design. Rotation is separate work. **Mitigation:**
  document explicitly in `deploy.md` that the secret is set at
  bootstrap and rotation requires a new tool (out of scope here).

\newpage

# 11. Out of scope

- Deploy automation (PowerShell wrapper that orchestrates publish +
  Migrator + IIS recycles). Manual sequence for now.
- Customer-environment provisioning automation. Same Migrator works;
  the per-customer orchestration around it is later work.
- Heimdall keypair rotation tooling.
- OIDC signing certificate rotation tooling.
- Replacing the Heimdall dev keypair with a Staging-specific one
  (tracked debt, separate from this plan).

\newpage

# 12. Order of operations

1. Workstream A (extract bootstrapper, no behaviour change).
2. Workstream B (Migrator commands wired up but old startup paths
   still active — both paths work simultaneously).
3. Workstream C (validator entries on Migrator).
4. Workstream F (integration test green).
5. Workstream D (rip out old startup paths once F is green).
6. Workstream E (dev script update — must follow D or `-Reset` would
   be redundantly bootstrapping twice).
7. Workstream G (docs).

Each workstream lands as one commit. Workstreams A through C ship
together if convenient; D depends on F being green.

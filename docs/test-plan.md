# Enki — System Test Plan

**For Gavin.** A guide to walking through every feature of Enki on a fresh build, with enough domain context that you finish the doc with a working mental model of *why* the application is shaped this way — not just *what* it does.

This document is your testing checklist **and** a guided tour of the codebase. Wherever a test exercises a recognisable software pattern (optimistic concurrency, RFC 7807, role-based authorization, JWT bearer auth, etc.), I name the pattern. Wherever the underlying behaviour lives in a specific file, I cite the path + line so you can read the source after running the test. Two birds.

---

## How to read this doc

| Section | Purpose |
|---|---|
| 1. Build under test | What hash you're verifying. |
| 2. System overview | The four hosts + per-tenant DB shape. |
| 3. Domain primer | Drilling concepts you need (read this first if you're new). |
| 4. Setup + sign-in | How to launch the dev rig and log in. |
| 5. Test-ID conventions | How to read / track / report test results. |
| 6. Smoke tests | A 15-minute sanity pass. Do this first on a fresh build. |
| 7+. Per-feature tests | Auth → tenants → jobs → wells → surveys → runs → shots → tubulars → formations → common measures → magnetics → calibrations → licenses → admin → audit → account → cross-cutting → cross-tenant isolation. |
| 99. Glossary | Drilling + software terms. Jump back here when something's unfamiliar. |

**How to log a result:** for each test row, replace the `[ ]` checkbox with `[x]` if it passed and `[F]` if it failed. If it failed, file a GitHub issue at <https://github.com/KingOfTheGeeks/Enki/issues> with the test ID in the title (e.g. *"TEN-12: Submit valid tenant returns 500"*) and the request/response or screenshot. Reference issues #8–#19 for examples of the shape Mike's worked from in past passes.

---

## 1. Build under test

| What | Where |
|---|---|
| Enki commit | Whatever's checked out — `git rev-parse HEAD` should match the build you're running. |
| Marduk commit | Sibling repo at `../Marduk/Marduk/`; `git rev-parse HEAD` from inside it. |
| .NET SDK | 10.0.202 (per `global.json`). |
| Test target | `scripts/start-dev.ps1 -Reset` from the Enki repo root. |

If the dev rig won't start, double-check the **Prerequisites** in the README before reporting a bug — most "won't start" reports trace back to SQL Server not running, the Marduk path being wrong, or `EnkiMasterCs` not being set.

---

## 2. System overview

Enki is a four-host system. You'll be exercising all of them.

```
┌─────────────────────────────────────────────────────────────────┐
│  Browser (you)                                                   │
└──────────┬──────────────────────────────────────────────────────┘
           │ cookie auth + HTML
           ▼
┌─────────────────────┐  bearer JWT   ┌─────────────────────────┐
│ SDI.Enki.BlazorServer│ ────────────▶│ SDI.Enki.WebApi          │
│ (port 5073)          │              │ (port 5107)              │
│ Cookie auth, SSR +   │              │ REST API. Per-tenant DB  │
│ InteractiveServer.   │◀───── 204 ───│ via routing middleware.  │
└──────────┬──────────┘              └─────────┬───────────────┘
           │ OIDC                                │ EF Core
           ▼                                     ▼
┌─────────────────────┐              ┌──────────────────────────┐
│ SDI.Enki.Identity    │              │ SQL Server               │
│ (port 5196)          │              │ Enki_Master              │
│ OpenIddict + ASP.NET │              │ Enki_Identity            │
│ Identity. Issues JWT.│              │ Enki_<CODE>_Active       │
└──────────────────────┘              │ Enki_<CODE>_Archive (RO) │
                                       └──────────────────────────┘
```

**Why three databases instead of one?** Enki replaces a legacy system (Athena) where every Job got its own DB — the schema-drift nightmare that caused. We went the other direction: one shared schema per **tenant** (customer organisation), not per Job. Each tenant gets a pair (Active read-write + Archive read-only) and a separate Master DB holds the registry of which tenants exist. Identity is a third DB so you can drop Enki's data without nuking everyone's passwords.

Cross-tenant data leakage is **impossible by construction** because the connection literally targets a different database for each tenant. There's no `WHERE TenantId = ?` to forget. The `TenantRoutingMiddleware` resolves the `{tenantCode}` URL segment to a connection string at the start of every request.

---

## 3. Domain primer

If you've never worked with directional drilling data, read this section once. Skip it on subsequent passes.

### What's a survey?

A wellbore is a long curved tube going from the surface into the earth (sometimes 5+ kilometres, often horizontal at the bottom). To know where it goes, you measure **Depth, Inclination, Azimuth** at each station along the way:

- **Depth** (or *Measured Depth* / MD) — distance along the borehole from the surface. Always increases as you go down.
- **Inclination** — angle from vertical. 0° = straight down, 90° = horizontal.
- **Azimuth** — compass direction (0–360°, 0 = north, 90 = east).

That triple, captured every ~30 m / 100 ft, is a **Survey station**. The first station is special — the **Tie-On** — anchored at known surface coordinates (Northing, Easting, vertical reference, sub-sea reference).

From the (Depth, Inc, Az) sequence + the tie-on, the **minimum-curvature** algorithm computes everything else:

| Computed | What it means |
|---|---|
| TVD (true vertical depth) | How deep you actually are below the rotary, ignoring horizontal drift. |
| Sub-sea | TVD relative to mean sea level. |
| North / East | Local-grid offset from the tie-on. |
| Northing / Easting | Absolute grid coordinates (tie-on offset + local). |
| Dogleg severity (DLS) | How sharply the path bends, °/30 m or °/100 ft. |
| Build / Turn | Rate of inclination / azimuth change. |
| Vertical Section | 1-D progress toward a target azimuth. |

**Marduk** owns this math. Enki stores the inputs and renders the outputs; it doesn't compute anything itself.

### Wells, Jobs, Tenants

| Concept | Description |
|---|---|
| **Tenant** | A customer organisation. PERMIAN, NORTHSEA, BOREAL are the seeded demo tenants. Has its own DB pair. |
| **Job** | A drilling project under a tenant. Carries a unit-system preference (Field / Metric / SI), a region label, start/end timestamps. |
| **Well** | A wellbore under a Job. Most Jobs have a **Target** (the producer) + **Injection** (a parallel injector ~15 m below) + **Offset** (legacy neighbour for anti-collision). |
| **Run** | A logical grouping of captures under a Job. Type ∈ {Gradient, Rotary, Passive}. |
| **Shot** | One captured event under a Gradient or Rotary Run. Carries a binary capture file + JSON config + JSON result. Passive runs have no Shots — capture lives directly on the Run row. |
| **Log** | Sensor stream during trip in/out of hole. Independent of Shots. |
| **Tubular** | A piece of pipe (casing / liner / tubing / drill pipe / open hole). Has from-MD, to-MD, diameter, weight. |
| **Formation** | A geological layer the well passes through. From-TVD, to-TVD, name, "resistance" value. |
| **Common Measure** | Depth-ranged dimensionless multipliers used by signal processing. From-TVD, to-TVD, value (~1.0). |
| **Magnetic Reference** | Per-well declination / dip / total field. Surveys use these to convert tool-measured azimuth to grid azimuth. |
| **Calibration** | Per-tool calibration session — 25 binary captures (`0.bin` baseline + `1.bin..24.bin`) processed by Marduk to produce a calibration set. |

### Anti-collision

In a producing field there are often dozens of wells in close proximity. Drilling a new well, you must not collide with existing ones — at the worst, two wellbores intersecting can release a high-pressure fluid stream that destroys both wells. The Macondo blowout (2010) is the textbook example. PERMIAN's seeded demo includes a Macondo-style relief-well intercept; BOREAL has the SAGD producer/injector pair (a producer/injector held ~5 m apart over hundreds of metres of lateral — the canonical SDI MagTraC ranging scenario).

The **anti-collision scan** projects every offset well's trajectory relative to a target and reports the closest-approach distance at every depth. The **travelling cylinder** plot is the most common visualisation.

### Unit systems

| Preset | Length | Pressure | Temperature | Density |
|---|---|---|---|---|
| Field (US oilfield) | ft | psi | °F | ppg |
| Metric | m | bar | °C | kg/m³ |
| SI | m | Pa | K | kg/m³ |

**Storage convention:** the database always stores SI / metric. The Blazor UI converts at the rendering edge based on (Job preset OR User preference) — see `UnitPreferenceProvider` in `src/SDI.Enki.BlazorServer/Auth/`. This is why your account-level "Preferred unit system" can override per-Job presets without touching the data on disk.

> **Software pattern:** *unit projection at the boundary*. A common pattern in scientific software — store canonical units in the model, convert at I/O. Means you never have to migrate data when display preferences change.

---

## 4. Setup + sign-in

### One-time machine setup

See the [README](../README.md) for prerequisites and first-run steps. The short version:

```powershell
[Environment]::SetEnvironmentVariable(
  "EnkiMasterCs",
  "Server=localhost;Database=Enki_Master;Integrated Security=true;TrustServerCertificate=true;",
  "User")
```

### Launch + reset

```powershell
./scripts/start-dev.ps1 -Reset    # drop everything + reseed (do this for a clean test run)
```

Wait for all three hosts to log "Now listening on …". Hit <http://localhost:5073>. If the page renders without an error, sign in with one of the seeded users (see `IdentitySeedData.cs` for the roster — `mike.king` / `gavin.helboe` etc. with their dev passwords pinned).

### What "Reset" gives you

After `-Reset`:

- **3 demo tenants** auto-provisioned: PERMIAN, NORTHSEA, BOREAL. Each has its own DB pair.
- **Each tenant has a Job + Wells + tie-ons + surveys + tubulars + formations + common measures + magnetic reference**, modeled on real-world drilling shapes (PERMIAN = parallel laterals + Macondo-style relief; NORTHSEA = offshore parallel laterals + Wytch-Farm-style ERD; BOREAL = SAGD producer/injector pair).
- **Each well also has 1–3 randomised Runs and 5–25 Shots per Gradient/Rotary run**, with bin captures pulled from a 25-file pool. Each tenant gets a different shape (deterministic per-tenant seed — same shape every reset).

That's a *lot* of pre-populated data — by design, so every page in the app has something to render.

---

## 5. Test-ID conventions

Test rows look like this:

| ID       | Test                                                         | Pass |
| -------- | ------------------------------------------------------------ | ---- |
| AUTH-01  | Hit `/` while signed out → "Sign in to continue" card shows. | [ ]  |

| Field | Convention |
|---|---|
| ID | Three-letter prefix per area (`AUTH-`, `TEN-`, `JOB-`, `WELL-`, `SUR-`, `RUN-`, `SHOT-`, `TUB-`, `FRM-`, `CMM-`, `MAG-`, `CAL-`, `LIC-`, `ADM-`, `CC-` for cross-cutting), then a 2-digit number. Listed in roughly the order you'd execute them. |
| Test | Steps + expected. Read it as "do these steps; expect this." |
| Pass | `[ ]` empty → not run. `[x]` → passed. `[F]` → failed. |

When a test fails, file a GitHub issue using the **test ID in the title** so we can correlate. Add a screenshot if it's a UI bug, the response body if it's a 4xx/5xx surfaced as a banner.

---

## 6. Smoke test (15 min)

Run this first on every fresh build. If any of these fail, stop and report — the rest of the doc isn't worth your time until smoke is green.

| ID       | Test                                                                                                          | Pass |
| -------- | ------------------------------------------------------------------------------------------------------------- | ---- |
| SMK-01   | All three hosts come up (Identity / WebApi / Blazor) with no errors in their logs.                            | [ ]  |
| SMK-02   | <http://localhost:5073> renders the **Overview** page with the Sign-in card.                                  | [ ]  |
| SMK-03   | Click **Sign in** → redirected to Identity → sign in → returned to overview as authenticated.                 | [ ]  |
| SMK-04   | Sidebar shows **OVERVIEW** / **TENANTS** / **FLEET** groups (every signed-in user) plus **SYSTEM** for `enki-admin` only. The TENANTS group expands with per-tenant drill-in (Jobs / Wells / Runs / Members / Audit) when a tenant is in URL scope. | [ ]  |
| SMK-05   | `/tenants` lists 3 demo tenants (PERMIAN / NORTHSEA / BOREAL).                                                | [ ]  |
| SMK-06   | Click **PERMIAN** → drills into Jobs list with at least 1 Job.                                                | [ ]  |
| SMK-07   | Click the Job → drills into Wells with at least 3 wells (Target / Injection / Offset shape).                  | [ ]  |
| SMK-08   | Click any Well → see Surveys card with non-zero station count.                                                | [ ]  |
| SMK-09   | Click **Surveys** → grid loads, **TVD / N / E / DLS** columns are populated (not all zero).                   | [ ]  |
| SMK-10   | Sign out via top-bar → returned to Overview signed out, top-bar shows "Sign in".                              | [ ]  |

Smoke green → continue.

---

## 7. Authentication + authorization

### What you're testing

Enki uses **OpenID Connect (OIDC)** with the **authorization-code flow + PKCE** between Blazor and Identity. A signed-in user has a cookie on the Blazor side carrying the access token; outbound API calls forward that token as a Bearer header (the `BearerTokenHandler` DelegatingHandler, in `src/SDI.Enki.BlazorServer/Auth/BearerTokenHandler.cs`). The WebApi validates the JWT using OpenIddict's local validation.

> **Software pattern:** *separation of authority and resource server*. The Identity host issues tokens; the WebApi consumes them. They share no session — the JWT is the contract. Standard OAuth 2.0 / OIDC architecture.

### Tests

| ID       | Test                                                                                                                    | Pass |
| -------- | ----------------------------------------------------------------------------------------------------------------------- | ---- |
| AUTH-01  | Hit `/` while signed out → "Sign in to continue" card visible.                                                          | [ ]  |
| AUTH-02  | Below it, the **Enki lore** card renders ("Lord of the Sweet Deep…").                                                   | [ ]  |
| AUTH-03  | Click **Sign in** → redirected to Identity at `/connect/authorize`.                                                     | [ ]  |
| AUTH-04  | Identity login form — submit empty → form rejects (HTML5 `required`).                                                   | [ ]  |
| AUTH-05  | Submit wrong password 5 times → user gets locked out (default Identity lockout).                                        | [ ]  |
| AUTH-06  | Submit correct credentials → returned to `/` as authenticated; top-bar shows username.                                  | [ ]  |
| AUTH-07  | Refresh the page → still signed in (cookie persists).                                                                   | [ ]  |
| AUTH-08  | Open a fresh incognito tab → not signed in (cookie is per-profile).                                                     | [ ]  |
| AUTH-09  | Click **Sign out** → returned to `/` signed out; cookie gone (devtools → Application).                                  | [ ]  |
| AUTH-10  | After sign-out, hitting `/admin/users` redirects to sign-in (auth-required route).                                      | [ ]  |
| AUTH-11  | As a regular (non-admin) user, hit `/admin/users` → 403; the **SYSTEM** sidebar group is also hidden.                   | [ ]  |
| AUTH-12  | As an `enki-admin` user, the sidebar shows the **SYSTEM** group with Users / Licensing / Settings / Audit; each route loads. | [ ]  |

> **Curious why** **AUTH-11** doesn't hit a 500? Each `/admin/*` page carries `[Authorize(Roles = "enki-admin")]`; ASP.NET's authorization pipeline rejects missing-role requests with 403. Defense-in-depth: the sidebar also hides the SYSTEM group via `<AuthorizeView Roles="enki-admin">`, so a non-admin doesn't see the link in the first place — but a hand-typed URL still gets a 403, not a render.

---

## 8. Tenants

### What you're testing

A tenant is a customer organisation with its own DB pair. Master-registry CRUD lives at `/tenants` (`src/SDI.Enki.WebApi/Controllers/TenantsController.cs`). Each action has its own authorization policy:

| Endpoint | Policy | Who satisfies |
|---|---|---|
| `GET /tenants` | `EnkiApiScope` + in-method filter | Any signed-in user; sees only their tenants (admin sees all) |
| `GET /tenants/{code}` | `CanAccessTenant` | Tenant member or admin (Tenant-type users for their bound tenant) |
| `PUT /tenants/{code}` | `CanWriteMasterContent` | Office+ or admin |
| `POST /tenants` | `CanProvisionTenants` | Supervisor+ or admin |
| `POST /tenants/{code}/deactivate` | `CanManageTenantLifecycle` | Supervisor+ or admin |
| `POST /tenants/{code}/reactivate` | `CanManageTenantLifecycle` | Supervisor+ or admin |

> **Software pattern:** *parametric requirement + single handler*. All twelve named policies in `SDI.Enki.Shared.Authorization.EnkiPolicies` are constructed from one `TeamAuthRequirement` record (carrying `MinimumSubtype` / `GrantingCapability` / `TenantScoped` / `RequireAdmin` flags) evaluated by a single `TeamAuthHandler` with an 8-step decision tree. Adding a new policy is a one-line registration in `Program.cs`, not a new handler class. See `src/SDI.Enki.WebApi/Authorization/TeamAuthRequirement.cs` and the matrix in [`docs/sop-authorization-redesign.md`](sop-authorization-redesign.md).

### Tests

| ID       | Test                                                                                                                                                        | Pass |
| -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| TEN-01   | Sign in as `enki-admin` → `/tenants` lists PERMIAN, NORTHSEA, BOREAL with status pills.                                                                     | [ ]  |
| TEN-02   | Click any tenant code → drills into `/tenants/{code}`.                                                                                                      | [ ]  |
| TEN-03   | Detail page shows Code (read-only), Name, Display name, Status, Active+Archive DB names, action buttons.                                                    | [ ]  |
| TEN-04   | Click **Edit** → `/tenants/{code}/edit` form pre-fills with current values.                                                                                 | [ ]  |
| TEN-05   | Change Display name → **Save** → returns to detail with new value.                                                                                          | [ ]  |
| TEN-06   | Refresh → value persists.                                                                                                                                   | [ ]  |
| TEN-07   | Edit two browser tabs simultaneously, save in one, then save in the other → second one shows a 409 conflict banner with field-level message.                | [ ]  |
| TEN-08   | Click **+ New tenant** → `/tenants/new` form. Submit empty → validation messages on required fields.                                                        | [ ]  |
| TEN-09   | Submit valid form (Code `TESTCO`, Name `Test Co`) → redirects to new tenant's detail page.                                                                  | [ ]  |
| TEN-10   | Re-submit with the same Code → 409; not added (Code uniqueness).                                                                                            | [ ]  |
| TEN-11   | On `TESTCO` detail → click **Deactivate** → status → Inactive.                                                                                              | [ ]  |
| TEN-12   | Try to navigate to `/tenants/TESTCO/jobs` while it's Inactive → 404 ProblemDetails (deactivation is a hard revocation; `TenantRoutingMiddleware`).          | [ ]  |
| TEN-13   | Click **Reactivate** → status → Active.                                                                                                                     | [ ]  |
| TEN-14   | Now `/tenants/TESTCO/jobs` is reachable again.                                                                                                              | [ ]  |
| TEN-15   | Sign in as a regular user (non-admin, non-member of `TESTCO`) → `/tenants` does **not** show TESTCO.                                                        | [ ]  |
| TEN-16   | While signed in as that user, type `/tenants/TESTCO` directly into the URL → 403.                                                                           | [ ]  |
| TEN-17   | Same user — try **Edit** / **Deactivate** / **Reactivate** / **Provision** via direct URL or curl → 403.                                                    | [ ]  |

### Tenant members

A separate sub-page at `/tenants/{code}/members` controls membership of a tenant. The per-tenant Admin/Contributor/Viewer role on a membership has been **retired** in the authorization redesign — membership is now a simple boolean (member or not), and management is gated by the system-wide `TeamSubtype` hierarchy. CRUD via `TenantMembersController`; UI in `TenantMembers.razor`. Authorization: `CanManageTenantMembers` — Supervisor+ tenant member or `enki-admin`; `CanAccessTenant` for read.

| ID       | Test                                                                                                                                                        | Pass |
| -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| TM-01    | Sidebar **Members** link (under TENANTS, when scoped) navigates to `/tenants/{code}/members`. Page lists current members with Username / Since columns. (Role column removed in the redesign.) | [ ]  |
| TM-02    | "Add member" form lists candidate users (the master roster minus current members). Submit empty → button disabled.                                          | [ ]  |
| TM-03    | Pick a user, submit → row appears in the grid; user disappears from the candidate dropdown. (No role selection — role is gone.)                            | [ ]  |
| TM-04    | *(Removed in the redesign — there is no per-member role to change. Repurpose this row when a future per-tenant capability surface lands.)*                  | n/a  |
| TM-05    | Click **Remove** on a member → confirms → row drops; user reappears in the candidate dropdown.                                                              | [ ]  |
| TM-06    | Open two browser tabs and add the same member from both. The first add wins; the second returns a 409-style conflict (member already exists).               | [ ]  |
| TM-07    | Sign in as a Field-tier tenant member → `/tenants/{code}/members` redirects them to `/forbidden?required=Supervisor&resource=Members+%2F+{code}` via the OnInitializedAsync probe. (Office is also blocked from member management — only Supervisor+ tenant members or admins satisfy.) | [ ]  |
| TM-08    | Sign in as a member of a different tenant → `/tenants/{code}/members` redirects to `/forbidden` (the membership probe fails before any data renders).      | [ ]  |

---

## 9. Jobs

### What you're testing

Jobs are tenant-scoped projects. CRUD at `/tenants/{tenantCode}/jobs` (`src/SDI.Enki.WebApi/Controllers/JobsController.cs`). Job status follows a small lifecycle (Draft → Active → Archived) gated by `JobLifecycle.CanTransition` (in `SDI.Enki.Core/TenantDb/Jobs/Enums/`). Authorization: `CanAccessTenant` — tenant member or admin.

> **Software pattern:** *finite-state machine for entity lifecycle*. Common pattern when an entity has a small set of states with enforced transitions. The `JobLifecycle.AllowedTransitions` table is the authoritative source; controller and UI both read it.

### Tests

| ID      | Test                                                                                                                                  | Pass |
| ------- | ------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| JOB-01  | `/tenants/PERMIAN/jobs` lists at least 1 Job; columns: Name, Well, Region, Units, Status, Start.                                      | [ ]  |
| JOB-02  | Grid is sortable + filterable (click a header → sort; filter row at top → type to filter).                                            | [ ]  |
| JOB-03  | Click a Job's name → drills into `/tenants/PERMIAN/jobs/{guid}`.                                                                      | [ ]  |
| JOB-04  | Detail page shows Name, Description, Unit system, Region, Wells count, per-type Run counts.                                           | [ ]  |
| JOB-05  | Click **Edit** → form pre-fills.                                                                                                      | [ ]  |
| JOB-06  | Change Description → Save → returns to detail.                                                                                        | [ ]  |
| JOB-07  | On a Draft Job → click **Activate** → status flips. Verify the Activate button is gone and Archive is now offered.                    | [ ]  |
| JOB-08  | On an Active Job → click **Archive** → status flips to Archived; Archive button gone; only "Restore" remains.                         | [ ]  |
| JOB-09  | On an Archived Job, try to PUT updates via the Edit page → 409 conflict (archived jobs are read-only; `JobsController.Update`).       | [ ]  |
| JOB-10  | Click **+ New job** → `/tenants/{code}/jobs/new` form. Submit empty → validation. Submit valid → redirects to new Job's detail.       | [ ]  |
| JOB-11  | The new Job's UnitSystem option list matches what the API exposes (`Field`, `Metric`, `SI`).                                          | [ ]  |
| JOB-12  | Job edit submitted with stale RowVersion (use two tabs) → 409 conflict ("modified by another user — reload").                         | [ ]  |

---

## 10. Wells

### What you're testing

Wells are children of a Job. Each Well carries a tie-on, surveys, tubulars, formations, common measures, and a magnetic reference. The first survey in a well drives Marduk's minimum-curvature calculation; the tie-on is the anchor.

| ID       | Test                                                                                                                                                          | Pass |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| WELL-01  | `/tenants/PERMIAN/jobs/{jobId}/wells` lists Target / Injection / Offset wells.                                                                                | [ ]  |
| WELL-02  | Each row links to `/tenants/.../wells/{wellId}`.                                                                                                              | [ ]  |
| WELL-03  | Detail page shows Name, Type, parent-Job link, **stat tiles** (station count, min/max depth), and child cards (Surveys, Tubulars, Formations, CMM, Magnetics). | [ ]  |
| WELL-04  | Click **Plot** (the Plan View / VSect / Travelling Cylinder card) → trajectory chart renders on the **Plan view** tab.                                        | [ ]  |
| WELL-05  | Plot axes use the Job's unit system (or your account's preferred unit if set on `/account/settings`).                                                         | [ ]  |
| WELL-06  | Set your account's Preferred unit system → revisit the plot → axes change without re-signing in.                                                              | [ ]  |
| WELL-07  | Click **+ New well** → form. Submit empty → required-field validation. Submit valid (Name, Type, parent Job auto-set) → new well appears in the Wells list.  | [ ]  |
| WELL-08  | Edit an existing well's name → save → returns to detail page; new value persists across refresh.                                                              | [ ]  |
| WELL-09  | Edit a well's Name to the empty string → 400 with field-level error.                                                                                          | [ ]  |
| WELL-10  | Switch to the **Travelling cylinder** tab → anti-collision scan loads (lazy first-fetch); offset wells render as polylines around the target's depth axis.   | [ ]  |
| WELL-11  | Click **Re-run scan** on the cylinder tab after editing a target survey → scan re-fetches and the polylines update to match the new trajectory.              | [ ]  |

---

## 11. Surveys (the trajectory grid)

### What you're testing

The most data-dense page in the app. Combines the Tie-on (top row) and Survey stations (rest) in one grid. **Both are inline-editable** — double-click any row, edit the observed fields (Depth / Inclination / Azimuth), Enter to save. Auto-calc fires server-side and the page reloads to show recomputed columns.

> **Software pattern:** *optimistic concurrency control via rowversion*. SQL Server's `rowversion` column is auto-incremented on every UPDATE. The client round-trips the last-seen value; the UPDATE's WHERE clause checks it. If somebody else saved first, our UPDATE affects 0 rows and EF throws `DbUpdateConcurrencyException`. We catch it and surface a 409. See `src/SDI.Enki.WebApi/Concurrency/ConcurrencyHelper.cs`.

### Tests

| ID       | Test                                                                                                                                          | Pass |
| -------- | --------------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| SUR-01   | `/tenants/PERMIAN/jobs/{jobId}/wells/{wellId}/surveys` shows the tie-on row at the top, surveys below, in depth order.                        | [ ]  |
| SUR-02   | Computed columns (TVD / N / E / DLS / V-sect / Build / Turn) populated for every survey row (the auto-calc fired on seed).                    | [ ]  |
| SUR-03   | Tie-on row's TVD = 0 (it IS the anchor).                                                                                                      | [ ]  |
| SUR-04   | Double-click a survey row → it enters edit mode. Editable: Depth, Inc, Az. Read-only in edit mode: TVD, Sub-sea, Northing, Easting.           | [ ]  |
| SUR-05   | Change Inclination on a station → press Enter → row saves; whole grid reloads with recomputed columns reflecting the change.                  | [ ]  |
| SUR-06   | Try to edit a survey's Depth to match an existing tie-on or another survey's depth → 400 with field-level error ("Depth N already exists…").  | [ ]  |
| SUR-07   | Edit the **tie-on** row → change Depth to a value that matches an existing survey → after save, that survey is removed (issue #11 behaviour). | [ ]  |
| SUR-08   | Edit the tie-on Depth to a value GREATER than several surveys → those surveys (depth ≤ tie-on) are pruned; deeper surveys stay.               | [ ]  |
| SUR-09   | Click **Import surveys** → file picker. Drop in a sample WITSML / CSV → modal shows detected format + units + warnings.                       | [ ]  |
| SUR-10   | Confirm the import → surveys replace existing; auto-calc fires; computed columns populated.                                                   | [ ]  |
| SUR-11   | Re-import a file when the well's tie-on has non-default values → 409 conflict modal showing existing vs. imported tie-on diff.                | [ ]  |
| SUR-12   | Choose **Keep existing** in the conflict modal → the file's tie-on is ignored; only stations import.                                          | [ ]  |
| SUR-13   | Click **Clear surveys** → all surveys (and the tie-on) drop; well returns to "no surveys" state.                                              | [ ]  |
| SUR-14   | Edit a survey value with two browser tabs open simultaneously: save in tab A, then in tab B → tab B shows a 409.                              | [ ]  |
| SUR-15   | Type `90.01` into an Inclination cell — value commits cleanly on Tab/Enter (does not revert; issue #10 fix).                                  | [ ]  |
| SUR-16   | From the Surveys grid header tie-on row, click the depth link → standalone tie-on edit page (`/tieons/{id}/edit`) opens with values pre-filled. | [ ]  |
| SUR-17   | Save the standalone tie-on edit → returns to dedicated TieOns list (`/wells/{id}/tieons`); list shows the updated tie-on; per-well auto-recalc fires for the surveys. | [ ]  |
| SUR-18   | The dedicated `/wells/{id}/tieons` page lists every tie-on row, supports + New / Edit; depth columns honour the Job's unit system (or account override). | [ ]  |

---

## 12. Runs + Shots + Logs

### What you're testing

Runs are children of Jobs (`/tenants/{code}/jobs/{jobId}/runs`). Type ∈ {Gradient, Rotary, Passive}. Gradient + Rotary runs carry **Shots**. Passive runs do not — their captured data lives directly on the Run row. All Run types can also carry **Logs** (sensor-stream files independent of Shots).

> **Software pattern:** *single-table inheritance with a discriminator + tagged-state fields*. Run is one entity with a `Type` discriminator and nullable Passive-only columns. Trade-off: the table has columns that don't apply to every row (Passive\* columns are null on Gradient / Rotary). Alternative would be three separate tables — clean but cross-type queries (`SELECT * FROM Runs WHERE JobId = ?`) become 3 queries + UNION. Single-table won here.

### Tests

| ID       | Test                                                                                                                          | Pass |
| -------- | ----------------------------------------------------------------------------------------------------------------------------- | ---- |
| RUN-01   | `/tenants/PERMIAN/jobs/{jobId}/runs` lists 1–3 runs per Job (the seeder generates a random count).                            | [ ]  |
| RUN-02   | Each row shows Name, Type, StartDepth, EndDepth, Status, StartTimestamp.                                                      | [ ]  |
| RUN-03   | Click **+ New run** → form. Submit valid → creates the run and returns to the detail page.                                    | [ ]  |
| RUN-04   | On a Run detail page, lifecycle buttons appear based on current status (Start / Suspend / Complete / Cancel / Restore).       | [ ]  |
| RUN-05   | Click each lifecycle button → status updates per `RunLifecycle.AllowedTransitions`.                                           | [ ]  |
| RUN-06   | A Gradient or Rotary Run lists its Shots in a child grid.                                                                     | [ ]  |
| RUN-07   | Each Shot row shows Name, FileTime, Binary size (bytes), Result status.                                                       | [ ]  |
| RUN-08   | Click a Shot → drills into shot detail. Binary download link works.                                                           | [ ]  |
| SHOT-01  | A Passive Run has **no Shots grid** — instead, the capture is on the Run row itself (PassiveBinary download link).            | [ ]  |
| SHOT-02  | Click **+ New shot** under a Gradient run → form prompts for ShotName + binary upload.                                        | [ ]  |
| SHOT-03  | Upload a `.bin` file → Shot row appears with Binary populated.                                                                | [ ]  |
| SHOT-04  | On a Shot detail page, click **Edit** → form pre-fills; change ShotName and save → returns to detail page with new value.    | [ ]  |
| RUN-09   | On a Run detail page, click **Edit** → form pre-fills; edit name / description (not lifecycle) → save persists.               | [ ]  |
| LOG-01   | Logs grid under any Run lists the seeded logs (or empty if none).                                                             | [ ]  |
| LOG-02   | Click **+ New log** → upload a binary → log appears.                                                                          | [ ]  |
| LOG-03   | On a Log detail page, click **Edit** → form pre-fills (shot name, calibration, file time); save persists.                     | [ ]  |
| LOG-04   | Log detail page download buttons (binary / config / result file) deliver the right file.                                      | [ ]  |

---

## 13. Tubulars

### What you're testing

Pipe segments inside the wellbore. CRUD lives at `/tenants/{code}/jobs/{jobId}/wells/{wellId}/tubulars` (`src/SDI.Enki.WebApi/Controllers/TubularsController.cs`). The list grid supports inline edit + inline delete via Syncfusion's native machinery.

| ID       | Test                                                                                                              | Pass |
| -------- | ----------------------------------------------------------------------------------------------------------------- | ---- |
| TUB-01   | Tubulars list under a seeded well shows multiple rows (casing / liner / tubing).                                  | [ ]  |
| TUB-02   | Columns: Order (#), Type, Name, From MD, To MD, Diameter (in / mm via OverrideUnit), Weight.                      | [ ]  |
| TUB-03   | Diameter column displays in **inches** when Job UnitSystem = Field, **mm** when Metric / SI.                      | [ ]  |
| TUB-04   | Double-click a row → enters edit mode; all editable cells are inputs.                                             | [ ]  |
| TUB-05   | Edit Type via dropdown (Casing / Liner / Tubing / DrillPipe / OpenHole) → Update → row reloads with new value.    | [ ]  |
| TUB-06   | Toolbar **Delete** → confirm dialog → row deletes and grid reloads.                                               | [ ]  |
| TUB-07   | Edit Diameter to an obviously wrong value (e.g. negative) → 400 from API surfaces in the banner.                  | [ ]  |
| TUB-08   | Click **+ New tubular** (toolbar) → standalone create form. Submit valid → new tubular appears in the grid.       | [ ]  |
| TUB-09   | Standalone create form: From-MD ≥ To-MD → 400 field-level error before save.                                       | [ ]  |

---

## 14. Formations + Common Measures

### What you're testing

Two grids with the same shape: From-TVD + To-TVD + a value (Resistance for Formations, Signal-factor multiplier for Common Measures). Same inline-edit + inline-delete pattern as Tubulars.

| ID       | Test                                                                                                                                  | Pass |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| FRM-01   | Formations list shows the seeded formations with names like "Eagle Ford", "Wolfcamp", "Sherwood".                                     | [ ]  |
| FRM-02   | Inline edit a Formation's Name + To-TVD → Update → row reloads.                                                                       | [ ]  |
| FRM-03   | Try to set From-TVD > To-TVD → 400 with field-level error ("FromVertical must be ≤ ToVertical").                                      | [ ]  |
| FRM-04   | Description preservation: the inline grid doesn't show Description, but editing a row preserves it (PUT does a detail-fetch first).   | [ ]  |
| FRM-05   | **+ New formation** form. Submit valid → redirects to list; new row appears.                                                          | [ ]  |
| FRM-06   | Toolbar **Delete** on a row → confirm → deletes.                                                                                      | [ ]  |
| CMM-01   | Common Measures list shows seeded entries with signal factors near 1.0.                                                               | [ ]  |
| CMM-02   | Inline edit + delete behave the same as Tubulars / Formations.                                                                        | [ ]  |
| CMM-03   | From-TVD > To-TVD → 400 (same `ValidateDepthRange` helper underneath).                                                                | [ ]  |
| CMM-04   | Click **+ New common measure** (toolbar) → standalone create form. Submit valid → new row appears in the grid.                        | [ ]  |

---

## 15. Magnetic Reference

### What you're testing

The well's geomagnetic correction (declination + dip + total field). PUT is upsert (works whether the row exists or not). DELETE is idempotent (works whether the row exists or not).

| ID       | Test                                                                                                                | Pass |
| -------- | ------------------------------------------------------------------------------------------------------------------- | ---- |
| MAG-01   | `/wells/{id}/magnetics/edit` → form pre-fills with the seeded values for the well.                                  | [ ]  |
| MAG-02   | Edit Declination + save → returns to well detail; the new value persists.                                           | [ ]  |
| MAG-03   | Refresh after save → value still there (no revert; issue #8 fix).                                                   | [ ]  |
| MAG-04   | On a well that has no magnetics yet → form is blank → submit valid → upserts (creates the row).                     | [ ]  |
| MAG-05   | Click **Clear magnetic reference** → confirm-twice button → row removed; well now has no magnetics.                 | [ ]  |
| MAG-06   | Concurrency: open two tabs editing the same magnetics → save in one then the other → second tab shows 409 conflict. | [ ]  |

---

## 16. Calibrations + Calibration wizard

### What you're testing

A calibration is a session of 25 binary captures (`0.bin` baseline + `1.bin..24.bin`) that Marduk processes to produce a calibration set. The wizard is at `/tools/{serial}/calibrate` (`src/SDI.Enki.BlazorServer/Components/Pages/ToolCalibrate.razor`).

| ID       | Test                                                                                                            | Pass |
| -------- | --------------------------------------------------------------------------------------------------------------- | ---- |
| CAL-01   | `/tools` lists the seeded fleet (22 tools + their calibration counts).                                          | [ ]  |
| CAL-02   | Click a tool → detail page lists the tool's calibrations grid; current calibration is flagged with a pill.      | [ ]  |
| CAL-03   | Click **+ Calibrate** on an Active tool → wizard opens.                                                         | [ ]  |
| CAL-04   | Wizard step 1 — drop in 25 `.bin` files (use the seed pool at `Data/Seed/BinaryFiles/` if you don't have your own). Wizard accepts them and shows progress.       | [ ]  |
| CAL-05   | If you upload only 24 (missing `0.bin`) → wizard 400s with a clear error.                                       | [ ]  |
| CAL-06   | After processing, wizard shows shot grid with NarrowBand stats per shot. Operator picks shots.                  | [ ]  |
| CAL-07   | Click **Compute** → Marduk produces the calibration; preview is shown.                                          | [ ]  |
| CAL-08   | Click **Save** → calibration persists; tool's "current" calibration updates.                                    | [ ]  |
| CAL-09   | Tool detail's calibration grid now shows the new row with current pill; previous current shows "Superseded".    | [ ]  |
| CAL-10   | Tick two calibrations → click **Compare selected** → side-by-side view at `/tools/{serial}/calibrations/compare`. | [ ]  |
| CAL-11   | Click a calibration in the tool's grid → CalibrationDetail page (`/calibrations/{id}`) renders metadata + the per-shot grid; current pill matches the tool detail's flagging. | [ ]  |
| CAL-12   | CalibrationDetail page download links (per-shot binary / config / result) deliver the right files.               | [ ]  |
| TOL-01   | `/tools/new` form. Submit empty → required-field validation. Submit valid (Serial, DisplayName, Generation) → new tool appears in the fleet list. | [ ]  |
| TOL-02   | Re-submit with the same Serial → 409 (Serial uniqueness).                                                         | [ ]  |
| TOL-03   | Click a tool → ToolDetail. Click **Edit** → form pre-fills; change DisplayName / FirmwareVersion → save persists. | [ ]  |
| TOL-04   | On ToolDetail of an Active tool, click **Retire** (confirm prompt) → status flips to Retired; **+ Calibrate** is hidden.            | [ ]  |
| TOL-05   | On a Retired tool, click **Reactivate** → status flips back to Active; **+ Calibrate** reappears.                                   | [ ]  |

---

## 17. Licensing (Heimdall)

### What you're testing

Enki generates RSA-signed `.lic` files for the field-side Esagila tool. Each .lic ships with a sidecar `.key.txt` that contains the GUID license key the operator paired the file with at generation time. **Both files together** are the deliverable — the .lic alone won't validate.

`enki-admin` only.

| ID       | Test                                                                                                                | Pass |
| -------- | ------------------------------------------------------------------------------------------------------------------- | ---- |
| LIC-01   | `/licenses` lists existing license records.                                                                         | [ ]  |
| LIC-02   | Click **+ New license** → wizard. Pick a tenant, configure feature flags, set expiry, submit.                       | [ ]  |
| LIC-03   | New license appears in the list with status Active.                                                                 | [ ]  |
| LIC-04   | Click **Download .lic** → file downloads.                                                                           | [ ]  |
| LIC-05   | Click **Download key** → sidecar .key.txt downloads. Both filenames carry the license id.                           | [ ]  |
| LIC-06   | Verify the .lic via the Marduk reader (out-of-band; ask Mike) — should round-trip cleanly with the matching key.    | [ ]  |
| LIC-07   | Try the .lic with a key that doesn't match → reader rejects.                                                        | [ ]  |
| LIC-08   | As a non-admin user, hitting `/licenses` → 403.                                                                     | [ ]  |
| LIC-09   | On a LicenseDetail page (Active license), click **Revoke** → enter reason → confirm. Status flips to Revoked; RevokedAt + RevokedReason populate; Revoke button disappears. | [ ]  |
| LIC-10   | List view shows the revoked row with the Revoked status pill; counts on the stat tiles update.                       | [ ]  |

> **Software pattern:** *RSA-signed envelopes with a per-license key*. The .lic is signed with the SDI private key (in `dev-keys/private.pem` for dev). Esagila verifies with the matching public key. The sidecar key.txt is a salt that pairs with the operator-chosen GUID — same pattern as license keys in commercial software.

---

## 18. Admin

### What you're testing

`enki-admin`-gated routes covering Team-account management, system defaults, and the cross-tenant audit feeds. There is no `/admin` landing page — the sidebar's **SYSTEM** group is the entry point, with direct routes to each admin surface. Note: the **Licensing** group lives on its own (visible to Supervisor+ or holders of the `Licensing` capability), not under SYSTEM. The SYSTEM group ships exactly three items.

| ID       | Test                                                                                                                | Pass |
| -------- | ------------------------------------------------------------------------------------------------------------------- | ---- |
| ADM-01   | Sidebar **SYSTEM** group lists exactly three items: Users / Settings / Audit. (Hidden for non-admins. Licensing is in its own group, see LIC tests.) | [ ]  |
| ADM-02   | Click **Users** (or hit `/admin/users`) → grid lists every user (Team and Tenant) with admin / locked / active / type / subtype columns.            | [ ]  |
| ADM-03   | Click a user's name → user-detail page; profile fields, classification (UserType + TeamSubtype), admin-role toggle, lockout buttons, password reset, capability checkboxes (Special Permissions). | [ ]  |
| ADM-04   | Toggle admin role → confirms → user's `IsEnkiAdmin` flips. Their next sign-in materialises the new claim.           | [ ]  |
| ADM-05   | Reset a user's password → temporary password is shown on screen for the admin to hand off out-of-band.              | [ ]  |
| ADM-06   | Lock a user → their next sign-in attempt fails with "Account is locked out".                                        | [ ]  |
| ADM-07   | Unlock → next sign-in succeeds.                                                                                     | [ ]  |
| ADM-08   | An admin **cannot** revoke their own admin role (self-protection — returns 409).                                    | [ ]  |
| ADM-08a  | An admin **cannot** revoke a capability claim from themselves (same self-protection path — returns 409).            | [ ]  |
| ADM-08b  | An admin **cannot** change their own classification (UserType / TeamSubtype) — returns 409.                          | [ ]  |
| ADM-09   | `/admin/settings` lists system-wide defaults (region suggestions, etc.).                                            | [ ]  |
| ADM-10   | Edit a system setting → save → persists across restart.                                                             | [ ]  |
| ADM-11   | On a Team user's detail page, tick the **Licensing** capability checkbox → Save. Sign in as that user → the Licensing sidebar group now appears. Untick → sign in again → the group disappears. | [ ]  |
| ADM-12   | Create a new Tenant-type user (UserType = Tenant, bind to PERMIAN). Confirm the user can sign in and only their bound tenant is visible to them. | [ ]  |

---

## 19. Audit

### What you're testing

Audit captures every `IAuditable` mutation across the system. Three storage scopes:

- **Per-tenant `AuditLog`** — entity-level changes inside a tenant DB.
- **`MasterAuditLog`** — cross-tenant ops events + privilege denials in the Master DB.
- **`IdentityAuditLog`** + **`AuthEventLog`** — admin actions on user accounts + sign-in / token issuance / lockouts in the Identity DB.

Two surfaces:

- **Per-entity tile** on every detail page (Job / Well / Run / Shot / Log / Tenant) — lazy-reveal "Show recent activity" button. On reveal, fetches up to 500 rows and renders an SfGrid with paging, sort, and filter. **Smallest-grouping rule**: Wells and Runs roll up children that don't have their own audit tile (Wells → surveys / tubulars / formations / etc.; Runs → shots / logs); Job and Tenant tiles are entity-only because their children (Wells/Runs and Jobs respectively) own their own pages.
- **Admin landing** at `/admin/audit` (enki-admin only) — three feed cards (Master / Identity / Auth events) with 7-day counts and links to the full feeds.

> **Software pattern:** *two-phase capture in EF SaveChanges interceptor with best-effort write*. Phase 1 stamps `IAuditable` columns + snapshots pre-save state. Phase 1b runs the underlying SaveChanges so int-IDENTITY keys land. Phase 2 builds audit rows with the now-real IDs and writes them in a separate non-transactional save. Audit failures log a warning but do not fail the original mutation. See [`ArchDecisions.md`](ArchDecisions.md) decision 11.

### Tests

| ID       | Test                                                                                                                                              | Pass |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| AUD-01   | On a Job detail page, click **Show recent activity** → tile reveals; grid loads with rows for the Job only (no Well / Run children).              | [ ]  |
| AUD-02   | Same Job tile — re-click button → tile collapses; click again → reopens without re-fetching (cached).                                             | [ ]  |
| AUD-03   | On a Well detail page (one with seeded surveys / tubulars), reveal tile → rows include `Survey`, `Tubular`, `Formation`, `CommonMeasure` entries. | [ ]  |
| AUD-04   | Same Well tile — sort by **When**, filter **Entity** column for `Survey` → only survey rows visible.                                              | [ ]  |
| AUD-05   | Same Well tile — paging dropdown switches between page sizes; total count reflects the unfiltered set.                                            | [ ]  |
| AUD-06   | On a Run detail page, reveal tile → rows include the Run + its `Shot` / `Log` children.                                                           | [ ]  |
| AUD-07   | On a Shot or Log detail page, reveal tile → rows are leaf-entity only.                                                                            | [ ]  |
| AUD-08   | On a Tenant detail page, reveal tile → rows are Tenant-only (no Job descendants).                                                                 | [ ]  |
| AUD-09   | Edit a Job's description → reload its detail page → reveal tile → an `Updated` row appears with `ChangedColumns = Description`.                   | [ ]  |
| AUD-10   | Per-tenant `/tenants/{code}/audit` page lists every audit row across the tenant; sortable + filterable; paginated.                                | [ ]  |
| AUD-11   | `/admin/audit` (admin only) shows three cards: Master / Identity / Auth events with 7-day counts and "Latest" tags.                               | [ ]  |
| AUD-12   | Click **Master events** card → `/admin/audit/master` lists every Master-DB audit row.                                                             | [ ]  |
| AUD-13   | Click **Identity events** card → `/admin/audit/identity` lists every Identity-DB audit row (admin role grants, lockouts, password resets, etc.).  | [ ]  |
| AUD-14   | Click **Auth events** card → `/admin/audit/auth-events` lists sign-ins, token issuance, lockouts.                                                 | [ ]  |
| AUD-15   | Sign in as a non-admin → `/admin/audit` returns 403; the SYSTEM sidebar group is hidden.                                                          | [ ]  |
| AUD-16   | In any audit grid row, click **Show details** → JSON snapshots in **Old** / **New** values expand and round-trip valid JSON.                      | [ ]  |
| AUD-17   | An audit row's `ChangedColumns` chip list shows only fields that actually moved on an Update (not RowVersion, not unchanged columns).             | [ ]  |
| AUD-18   | From `/admin/audit/master`, click an entity reference → `/admin/audit/master/{type}/{id}` shows every audit row for that entity in chronological order. | [ ]  |
| AUD-19   | From `/admin/audit/identity`, click an entity reference → `/admin/audit/identity/{type}/{id}` shows every identity-DB audit row for that entity. | [ ]  |
| AUD-20   | From `/tenants/{code}/audit`, the tenant-wide page is sortable / filterable / paginated like the per-entity tile.                                  | [ ]  |

---

## 20. Account settings

### What you're testing

User-level preferences. Today there's one: **Preferred unit system** override. Setting it makes every page in the UI render in your preferred units regardless of the Job's preset. Storage stays SI.

| ID       | Test                                                                                                                          | Pass |
| -------- | ----------------------------------------------------------------------------------------------------------------------------- | ---- |
| ACC-01   | `/account/settings` form shows current preferred unit system.                                                                 | [ ]  |
| ACC-02   | Pick **Field** → save → "Settings saved" banner.                                                                              | [ ]  |
| ACC-03   | Navigate to any well's Surveys page → values render in **ft / °** (Field).                                                    | [ ]  |
| ACC-04   | Change preference to **Metric** → save → revisit Surveys → values now in **m / °**.                                           | [ ]  |
| ACC-05   | Reset preference to **— Use the per-Job default —** → save → next page reverts to whatever the Job's UnitSystem says.         | [ ]  |
| ACC-06   | Preference change takes effect on the **next navigation** (no sign-out / sign-in needed; the provider invalidates).           | [ ]  |

> **Software pattern:** *scoped service with explicit cache invalidation*. `UnitPreferenceProvider` is registered scoped (one per Blazor circuit). It caches the fetched preference once; AccountSettings calls `Invalidate()` after save so the next page picks up the change without restart.

---

## 21. Cross-cutting

| ID       | Test                                                                                                                          | Pass |
| -------- | ----------------------------------------------------------------------------------------------------------------------------- | ---- |
| CC-01    | Every error response is **RFC 7807 ProblemDetails** — never `{ error: "..." }` bare JSON. (Inspect via devtools → Network.)   | [ ]  |
| CC-02    | 404s carry `entityKind` + `entityKey` extension members.                                                                      | [ ]  |
| CC-03    | 400 validation errors carry `errors: { field: [messages] }` and the page renders per-field messages under the banner.         | [ ]  |
| CC-04    | 409 conflicts carry a human-readable `detail` and any structured conflict info as extension members.                          | [ ]  |
| CC-05    | Long-running endpoints (Provision, Import) honour `[RequestTimeout("LongRunning")]` (60-second cap).                          | [ ]  |
| CC-06    | Hot endpoints have rate-limit (`[EnableRateLimiting("Expensive")]`) — 5 reqs/min per user. 6th in a minute → 429.              | [ ]  |
| CC-07    | `/health/live` returns 200 immediately (no DB dep).                                                                           | [ ]  |
| CC-08    | `/health/ready` returns 200 only when the master DB connection is healthy. Stop SQL Server briefly → ready turns 503.         | [ ]  |
| CC-09    | API versioning: `?api-version=1.0` and `X-Api-Version: 1.0` both accepted; response carries `api-supported-versions: 1.0`.    | [ ]  |
| CC-10    | Audit columns: every IAuditable mutation populates UpdatedAt + UpdatedBy with the signed-in user's identifier.                | [ ]  |
| CC-11    | OpenAPI spec at `/openapi/v1.json` (Dev only) lists every endpoint with the right ProblemDetails status responses.            | [ ]  |
| CC-12    | Sort + filter on every list grid (Tenants / Jobs / Wells / Tools / Licenses / Tubulars / Formations / CommonMeasures / etc).  | [ ]  |
| CC-13    | RowVersion concurrency: every IAuditable entity has a working `rowversion` column (concurrent edits → 409 not last-write-wins). | [ ]  |
| CC-14    | Every list grid (Tenants / Jobs / Wells / Runs / Tools / Logs / Shots / Licenses / AdminUsers / TenantMembers) spans full container width with the first / link column absorbing leftover space — no dead space on the right, no truncated cell content. | [ ]  |

---

## 22. Cross-tenant isolation (the highest-stakes regression check)

### What you're testing

The single defect that would be most catastrophic: a user from Tenant A seeing or modifying Tenant B's data. Every test in this section is a regression check against that boundary.

> **Software pattern:** *isolation by construction, not by query filter*. Cross-tenant data leakage is impossible because each tenant's data lives in a different database — there's no `WHERE TenantId = ?` to forget. The boundary is the connection string. See `Isolation.Tests` in the test suite for the automated equivalent.

| ID       | Test                                                                                                                                        | Pass |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| ISO-01   | Sign in as a user who's a member of PERMIAN only. Try `/tenants/NORTHSEA/jobs` directly → 403.                                              | [ ]  |
| ISO-02   | Same user — `/tenants` lists only PERMIAN.                                                                                                  | [ ]  |
| ISO-03   | Same user — try to PUT a Job in NORTHSEA via curl with their token → 403.                                                                   | [ ]  |
| ISO-04   | Same user — anti-collision scan response only references wells in tenants they're a member of.                                              | [ ]  |
| ISO-05   | Inactive tenant: deactivate PERMIAN → `/tenants/PERMIAN/jobs` returns 404 even for `enki-admin` (admins reactivate via master endpoints).   | [ ]  |
| ISO-06   | Reactivate PERMIAN → routes work again.                                                                                                     | [ ]  |

---

## 99. Glossary

### Drilling

| Term | Meaning |
|---|---|
| MD | Measured Depth — distance along the borehole. |
| TVD | True Vertical Depth — straight-line depth from surface. |
| Inc | Inclination — angle from vertical (0° = straight down). |
| Az | Azimuth — compass direction (0° = north). |
| DLS | Dogleg severity — how sharply the path bends per unit MD. |
| Tie-on | Anchor station at known surface coords; first row in Surveys. |
| KB | Kelly Bushing — the rotary-table reference height, MD = 0. |
| WMM | World Magnetic Model — global declination/dip lookup. |
| ERD | Extended-Reach Drilling — laterals 5+ km long. |
| SAGD | Steam-Assisted Gravity Drainage — paired producer/injector wells, ~5 m apart. |
| Run | A grouping of capture events under a Job. |
| Shot | One captured event under a Run (Gradient/Rotary). |

### Software

| Term | Meaning |
|---|---|
| OIDC | OpenID Connect — identity layer over OAuth 2.0. |
| PKCE | Proof Key for Code Exchange — anti-interception extension to OIDC auth-code flow. |
| JWT | JSON Web Token — signed token format used to carry claims between hosts. |
| ProblemDetails | RFC 7807 — standardised error JSON shape. |
| RowVersion | SQL Server's auto-incremented binary column used for optimistic concurrency. |
| EF Core | Entity Framework Core — Microsoft's ORM. |
| InteractiveServer | Blazor render mode where the component runs server-side over a SignalR circuit. |
| SSR | Server-Side Rendering — page renders once on the server, then becomes static HTML. |
| Smart enum | A class-based replacement for C# enums that allows methods + variant inheritance. Library: `Ardalis.SmartEnum`. |
| Specification pattern | A way to encapsulate query criteria as objects. Deliberately **not** used here. |
| MediatR | A request/handler dispatch library. Deliberately **not** used here — controllers talk to DbContext directly. |

### Enki-specific

| Term | Meaning |
|---|---|
| `enki-admin` | Cross-tenant SDI-side operator role. Materialised at sign-in from `IsEnkiAdmin` column. |
| TenantUser | Row in master.TenantUsers linking an Identity user to a tenant with a per-tenant Role. |
| Marduk | Sibling repo at `../Marduk/Marduk/` — owns all drilling-domain math. Referenced via `<ProjectReference>`, not NuGet. |
| Heimdall | The license-file format Enki generates for Esagila. RSA-signed envelope + sidecar key. |
| Esagila | Field-side desktop tool that consumes Heimdall licenses. |
| Athena | The older legacy SDI system. Per-Job DB design — Enki's per-tenant DB pair design is in part a reaction to Athena's schema-drift pain. Enki replaces it. |
| Artemis | The .NET 8 monolith that succeeded Athena. No auth, MATLAB dep, computation duplicated outside Marduk. Enki replaces it too — and is the direct predecessor whose entities you'll see referenced in seed data + porting notes. |

---

*If you find anything in this doc that's wrong, out-of-date, or unclear, file an issue. The doc is a living artefact — it should keep up with the system it describes.*

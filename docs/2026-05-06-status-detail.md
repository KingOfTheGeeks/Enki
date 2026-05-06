---
title: "Enki — Weekly Status (Detail)"
subtitle: "Engineering deep-dive companion to 2026-05-06-status-summary.md"
author: "SDI · KingOfTheGeeks"
date: "2026-05-06"
---

# Enki — Weekly Status (Detail)

**Period:** Thursday 2026-04-30 12:00:00 -0600 — Wednesday 2026-05-06 17:36:15 -0600.
**Range:** `340bef6^..e5c2fc4` (88 commits).
**Branch:** `main` (with worktree work merged through `e5c2fc4`).

This document is the engineering deep-dive intended for internal review. The
short-form for the weekly meeting is in
[`2026-05-06-status-summary.md`](2026-05-06-status-summary.md).

---

# 1. Volume metrics

## 1.1 Aggregate

| Metric | Value |
|---|---:|
| Commits | **88** |
| Unique files modified | **445** |
| Lines inserted | **+38,590** |
| Lines deleted | **-11,015** |
| Net delta | **+27,575** |
| GitHub issues touched | **20** distinct (#12, #20, #21, #22, #23, #24, #25, #26, #27, #28, #30, #33, #34, #36, #39, #41, #42, #43, #44, #47) |
| AI-paired commits | 81 of 88 (92%) |

## 1.2 Daily cadence

| Date | Day | Commits |
|---|---|---:|
| 2026-04-30 | Thu | 13 |
| 2026-05-01 | Fri | 4 |
| 2026-05-02 | Sat | 21 |
| 2026-05-03 | Sun | 8 |
| 2026-05-04 | Mon | 5 |
| 2026-05-05 | Tue | 20 |
| 2026-05-06 | Wed | 18 |

Average ~12.6 commits/day across the seven-day window. Saturday and
Tuesday were the heaviest days; Friday and Monday the lightest.

## 1.3 Period split — feature work vs refactor

| Window | Files | Insertions | Deletions | Net | Character |
|---|---:|---:|---:|---:|---|
| **Apr 30 12:00 → May 6 12:00** | 326 | +29,860 | -3,285 | **+26,575** | Feature work + initial Blazor pilot |
| **May 6 12:00 → now** | 199 | +10,078 | -9,078 | **+1,000** | Mass `.razor` → `.razor.cs` refactor + audit + small fixes |

The today window is mostly **net-zero relocation** — the BlazorServer mass
refactor moved code from inline `@code` blocks in `.razor` files to
`.razor.cs` partial-class siblings without changing behaviour. So real
new functionality lands almost entirely in the six-day pre-refactor
window. The +27,575 raw-line figure overstates feature volume by the
relocation noise; the meaningful "new content" figure is closer to the
+26,575 from the first window.

## 1.4 Per-project breakdown (range diff)

| Project / area | Files | Insertions | Deletions | Net |
|---|---:|---:|---:|---:|
| `src/SDI.Enki.Core/` | 17 | +396 | -77 | +319 |
| `src/SDI.Enki.Shared/` | 35 | +1,101 | -158 | +943 |
| `src/SDI.Enki.Infrastructure/` | 26 | +1,501 | -447 | +1,054 |
| `src/SDI.Enki.WebApi/` | 39 | +2,201 | -616 | +1,585 |
| `src/SDI.Enki.BlazorServer/` | 193 | +12,676 | -9,007 | +3,669 |
| `src/SDI.Enki.Identity/` | 19 | +2,050 | -258 | +1,792 |
| `src/SDI.Enki.Migrator/` | 10 | +621 | -27 | +594 |
| **`src/` total** | **339** | **+20,546** | **-10,590** | **+9,956** |
| `tests/` | 54 | +7,309 | -274 | +7,035 |
| `docs/` + `user-docs/` | 17 | +4,129 | -77 | +4,052 |
| `scripts/` | 8 | +1,281 | -18 | +1,263 |

The BlazorServer project carries the largest churn (193 files / +12.7k /
-9.0k) almost entirely because of today's code-behind refactor; subtract
that and Blazor net-grew by ~2.7k LOC of real new content.

## 1.5 Codebase size at HEAD

| Project | LOC at HEAD |
|---|---:|
| `Core` | 3,376 |
| `Shared` | 3,633 |
| `Infrastructure` | 14,601 |
| `WebApi` | 9,808 |
| `BlazorServer` (`.cs`) | 12,227 |
| `BlazorServer` (`.razor`) | 11,317 |
| `Identity` | 5,876 |
| `Migrator` | 988 |
| **All `src/*.cs`** | **50,509** |
| **Tests (`.cs`)** | **18,521** |

## 1.6 Test counts

| Metric | Value |
|---|---:|
| Total `[Fact]` / `[Theory]` / `[SkippableFact]` at HEAD | **712** |
| New test files added in window | **26** |
| New test LOC | **+7,035** (net) |

Test growth is disproportionately large relative to source growth —
Test LOC grew by 7,035 against source-LOC growth of 9,956. The test
suite is approximately 36% the size of the source codebase by line
count. Coverage backfill across Phases 1–4 + 6 (commit `ef37b24`)
delivered +2,742 new test LOC in a single commit.

---

# 2. Themes (work organised by initiative)

The 88 commits fall into 15 thematic clusters. Each cluster lists the
material commits, what shipped, and any open follow-up.

## 2.1 Authorization redesign — *the largest single thread*

**Major commits:**

- `01206c2` `feat(authz): subtype + capability authorization with parametric policy` — 83 files, +4,174 / -861. **Single biggest feature commit of the period.** Replaces ad-hoc role checks with a parametric `TeamAuthRequirement` evaluated by one `TeamAuthHandler` against twelve named policies, plus the `EnkiApiScope` default. Introduces the two-axis (UserType / TeamSubtype) classification + capability claims (currently `licensing`).
- `0e812f3` `feat(users): edit profile + classify Team/Tenant + bind Tenant to one tenant` — 32 files, +3,608 / -56. Admin-side user CRUD; immutable UserType; Tenant-bound user surface.
- `bc24609` `fix(auth): close 6 access-control gaps surfaced by an end-to-end audit` — 24 files, +723 / -127. Fixes six gaps caught in the May 2 review.
- `59a22d7` `fix(auth): defence-in-depth on AdminUsersController + Bug G test + Tenant-user CanAccessTenant` — Closes the last bearer-token-isolation regression.
- `4f3fc26` `feat(authz): widen Logs writes to Field + Tenant-bound (rig-side parity)` — `LogsController` joins `Runs` / `Shots` on the class-level `CanAccessTenant` floor.
- `0480311`, `e3ef3c3`, `340bef6` — concurrency-token pin trio that hardened RowVersion + ConcurrencyStamp.

**Documentation:** `ef9d80e` (xmldoc + repo-wide alignment), `0b2bd4a` (Permissions Matrix initial), `8a10082` (matrix edit).

**SOPs covering this:**
- `docs/sop-authorization-redesign.md` (SOP-002) — promoted Draft → Active 2026-05-06.
- `docs/sop-gui-gating.md` (SOP-003) — promoted Draft → Active 2026-05-06.
- `docs/sop-security-testing.md` (SOP-004) — staging-walk, Active v2.2.
- `docs/Enki-Permissions-Matrix.md` — current as of 2026-05-06 audit.

**Status:** Live on `dev.sdiamr.com`. No follow-up identified.

## 2.2 Concurrency hardening (issue #20 et al.)

Optimistic-concurrency was structurally broken before this window —
the WebApi was setting only `CurrentValue` on the RowVersion,
leaving `OriginalValue` at the loaded-from-DB value, which silently
no-op'd the concurrency check.

**Material commits:**

- `340bef6` `fix(concurrency): pin OriginalValue, not just CurrentValue, on RowVersion` — Tenant-DB write surface.
- `e3ef3c3` `fix(concurrency): pin ConcurrencyStamp OriginalValue on admin user actions` — Identity DB.
- `0480311` `fix(concurrency): pin RowVersion on lifecycle endpoints` — `LifecycleTransitionDto` carries the token; Activate / Archive / Restore / Suspend / Complete / Cancel all enforce.
- `1be2115` `fix(captures): translate SQL FK / unique violations to clean 409 (closes #27)` — DbUpdateException translation extended to FK violations and uniqueness races.
- `4e18192` `fix(infra): translate Tenants.Code race to friendly 400` — **2026-05-06**. Closes the `IX_Tenants_Code` race window in `TenantProvisioningService.ProvisionAsync` with a deterministic `ProvisioningRaceSmoke` test.

**Documentation:** `d4224a9` (initial standalone test plan), promoted to
SOP-005 in `e7dcfa8`.

**Status:** Mechanism live. Test inventory of 70+ `CC-*` test IDs in
SOP-005 §A–§N covers the contract end-to-end. SOP-004 §9 carries 13
of those as the customer-staging walk.

## 2.3 Tenant routing + provisioning

**Material commits:**

- `0f04d30` `fix(tenants): bust resolved-tenant cache after Provision (issue #21)` — Cache-invalidate on lifecycle changes.
- `4bf113e` `fix(tenants): exempt master-registry routes from tenant routing (issue #23)` — `[SkipTenantRouting]` attribute pattern.
- `f45fa1b` `fix(tenants): pin duplicate-code Provision contract + RFC 7807 content-type (partial #22)` — Pre-check + ProblemDetails surface.
- `066c624` `fix(tenants): land on Overview when selecting a tenant, not Jobs (issue #41)` — UX nuance.
- `4e18192` (also covered in §2.2 — race-window fix).

**Status:** Tenant routing is now bullet-proofed end-to-end. Cross-
tenant isolation tests (`tests/SDI.Enki.Isolation.Tests`) and the
SEC-8.14 staging walk both pass.

## 2.4 Migrator-driven environment bootstrap (PLAN-002)

**Major commits:**

- `2323850` `feat(migrator): SDI-ENG-PLAN-002 environment bootstrap via Migrator CLI` — 24 files, +2,192 / -501. Adds `bootstrap-environment`, `migrate-identity`, `migrate-master`, `migrate-all` commands. Idempotent; create-only on the OpenIddict client.
- `c6cca7a` `feat(migrator): dev-bootstrap honors Identity:Seed:BlazorBaseUri` — Environment-driven OIDC redirect targets.
- `9febb81` `fix(migrator): unblock dev-bootstrap on the local rig` — Removes the dead `AddDefaultTokenProviders()` from the CLI host's DI graph.
- `8494021` `fix(identity): load OIDC PFX with MachineKeySet+EphemeralKeySet (IIS pool friendly)` — **Critical IIS gotcha fix.** PFX loader was tripping on the IIS app-pool virtual-account profile shape.

**Documentation:** `docs/plan-migrator-bootstrap.md` (PLAN-002, Implemented).

**Status:** First-time deploys now follow `Enki.Migrator
bootstrap-environment` followed by IIS pool start. Documented in
`docs/deploy.md`.

## 2.5 Required-secrets validation (PLAN-001 Workstream C)

**Material commits:**

- `ad22daa` `feat(security): SDI-ENG-PLAN-001 Workstream C — required-secrets startup validation` — Each host fail-fast on missing secrets in non-Development.
- `3179b2a` `docs(plan): SDI-ENG-PLAN-001 critical pre-deployment security plan + deploy.md secret-staging rewrite` — Documents the contract.

**Documentation:** `docs/plan-prototype-security.md` (PLAN-001 v1.2 as
of today), `docs/deploy.md` § Secret staging.

**Status:** Live on every host. PLAN-001 Workstream C marked
Implemented; A / B / D still open (see §6).

## 2.6 MD-canonical depth model

- `df95354` `feat(wells): MD-canonical depth model with survey-derived TVD` — 34 files, +1,345 / -235.
  Treats Measured Depth as canonical. TVD is interpolated from
  surveys, never directly entered. Tubulars, Formations, Common
  Measures bounded by Survey MD range. Foundation for future anti-
  collision work.

**Documentation:** Captured in the `reference_depth_model.md` agent
memory; surfaced in test-plan §3 (Domain primer) on the well-data
walkthrough.

## 2.7 Wells review polish (P0..P3)

Block of commits 2026-05-05 addressing the wells-review punch-list:

- `d8853fd` P0 — timestamps, tie-on label, surveys count.
- `68eb039` P1 — toolbar gating, plot polish, nav probe await.
- `fe2d46f` P2 — tile labels, dropdown copy, nT case.
- `4854f79` P3 — filter wiring, TC heading, Injection→Intercept.
- Plus `5f901be` (Job Start/End calendar dates), `654656d`
  (License.ExpiresAt calendar dates), `a0da575` (in-place grid
  refresh), `ff09ecd` (auto-save tie-on cells on blur), `988a3e2`
  (sidebar name fetch race fix).

**Status:** Punch-list cleared.

## 2.8 Injection → Intercept rename

Domain rename across every layer of the stack. Six commits:

- `35acea1` `fix(wells): finish Injection→Intercept rename — repair test assertions + stale doc comments`.
- `bb4df5f` `refactor(models): rename InjectionWell{,Id} → InterceptWell{,Id} on Rotary/Gradient models` — 6 files, +1,658 / -26 (mostly migration scaffolding).
- `b0e85e0` `chore(seed): finish Injection→Intercept rename in seed comments`.
- `4854f79` (the P3 commit also picks up the doc-side rename).
- `18b2c3c` cleans up orphan TieOns list/create flow that was tied to the old shape.
- `a0da575` (per-grid refresh refactor surfaces the rename consequences).

**Reason for the rename:** the previous `Injection` term was misleading
(it implied SAGD steam injection or fluid injection-side work, both of
which are different concepts). `Intercept` correctly describes the
relief-well / parallel-laterals pairing semantics.

## 2.9 Tools retirement workflow (issue #47)

- `82772b4` `feat(tools): structured retirement workflow with modal + audit columns` — 24 files, +2,222 / -115.

Adds Disposition (Retired / Lost / Scrapped / Sold / Transferred /
ReturnedToOwner), Effective date, Reason, Replacement-tool serial,
Final-location columns to `Tool`. Modal-based workflow with
self-protection (rejects self-replacement, validates replacement
serial exists). Six seeded retirement fixtures (1099001..1099006)
cover each Disposition flavour for SOP-004 row TOL-04f.

## 2.10 Calibrations

- `00bc08b` `feat(calibrations): add .mpf download on CalibrationDetail page (issue #36)` — Marduk MATLAB v7 calibration export.
- `9486030` `fix(licensing): pin dev keypair to Marduk's hardcoded public key` — Heimdall keypair alignment.
- `62ffd01` `fix(licensing): mark license download anchors as downloads (closes #28)` — Anchor `download` attribute on the license + sidecar key.
- `7ac725e` `fix(settings): validate calibration defaults at save time (issue #43)`.
- `41f2319` `feat(settings): per-key reset to default + compact single-line inputs (issue #44)`.

## 2.11 Identity / self-service

- `b4ff9d8` `feat(identity): per-user session lifetime override` — 18 files, +1,516 / -43. Mike at 1y, Gavin at 8h; demonstrates the override path.
- `1d86235` `feat(identity): self-service Change password card on account settings (issue #39)`.
- `1eed0e8` `fix(identity): shape /connect/* 429 as OAuth-JSON, raise limit (issue #24)` — RFC 6749-compliant rate-limit error responses.
- `00da776` `fix(auth): disable rolling refresh tokens to keep cookie's refresh_token reusable` — Critical: Blazor circuit cache can't persist a rotated refresh token mid-flow.
- `b3973c7` `fix(blazor): accept empty Contact Email on tenant forms` — **2026-05-06**. New `OptionalEmailAddressAttribute` in `SDI.Enki.Shared.Validation`.

## 2.12 IIS staging deploy

Block of commits 2026-05-03 / 2026-05-04 / 2026-05-05 that brought
`dev.sdiamr.com` online and operational:

- `72369ec` `chore(deploy): IIS Staging configs + publish output hygiene`.
- `e41b239` `feat(scripts): add start-staging.ps1 (IIS pool wrapper for staging rig)`.
- `cebf12f` `feat(scripts): make staging reset mirror dev (full roster + sidecar deploy)`.
- `6b02209` `fix(infra): enable Blazor WebSocket transport on IIS staging` — Blazor SSR over IIS-hosted WebSocket; required `install-iis-websockets.ps1`.
- `aad93eb` + `679e051` — Enki DB backup + SQL Agent install (T-SQL).
- `21e1273` `archived scripts to rebuild` — historical reference scripts (5,391 LOC; mostly archive material rather than new behaviour).

## 2.13 Test-coverage backfill

- `ef37b24` `test: backfill coverage across Phases 1-4 + 6 (P5 deferred)` — 14 files, +2,742 / -0.

26 new test files in total across the period (see §3 below for the
inventory). Test count grew from approximately 600 to 712 — a +18%
increase.

## 2.14 Documentation

Significant documentation throughput across the period:

- `ef9d80e` xmldoc + repo-wide doc alignment (post-authz).
- `0b2bd4a` Permissions Matrix initial + `8a10082` edits.
- `d4224a9` standalone concurrency test plan, later promoted to SOP-005 in `e7dcfa8`.
- `01d983b` SOP-004 security testing protocol initial issue.
- `3179b2a` PLAN-001 + `deploy.md` secret-staging rewrite.
- `6d23a86` rebuild markdown set against current code.
- `e7dcfa8` SOP-004 pivot to staging UI; promote concurrency plan to SOP-005.
- `d5df3a2` added human docs (.docx).
- `4954cc3` **2026-05-06** ArchDecision #12 (Migrator three-channel output).
- `e5c2fc4` **2026-05-06** repo-wide audit pass — 12 docs verified, 5 had factual drift fixed.

## 2.15 BlazorServer code-behind refactor (today)

Mass refactor moving every `.razor` page that still carried inline
`@code` blocks into `.razor` + `.razor.cs` partial-class code-behind.
Ten commits, in order:

| Commit | Scope | Files |
|---|---|---:|
| `ad8754e` | Pilot — `WellEdit.razor` | 2 |
| `46b0817` | `RedirectToLogin` | 2 |
| `c245983` | `NavMenu` | 2 |
| `b4f5b60` | `Editors/` | 4 |
| `3b11e1e` | `Audit/` | 8 |
| `f1832b0` | `Shared/` | 7 |
| `4c624d0` | `Pages/Wells/` | 20 |
| `f130dda` | `Pages/Runs/` | 12 |
| `545ae1d` | `Pages/Admin/` | 10 |
| `af43fb7` | top-level `Pages/` | 25 |

Total **90 new `.razor.cs` files**. Almost net-zero for line count
(content moved, not added) but a uniform shape across the host. Two
calibration-wizard pages (`ToolCalibrate.razor`, `CalibrationCompare.razor`)
deliberately retain their inline `@code` blocks because they host
markup-bearing `RenderFragment` helpers that genuinely require the
Razor compiler — documented inline.

---

# 3. New test files

26 net-new test files added across the period:

**`tests/SDI.Enki.BlazorServer.Tests/`** (3)
- `Api/HttpClientApiExtensionsTests.cs`
- `Auth/CircuitTokenCacheTests.cs`
- `Auth/UnitPreferenceProviderTests.cs`

**`tests/SDI.Enki.Core.Tests/`** (1)
- `Configuration/RequiredSecretsValidatorTests.cs`

**`tests/SDI.Enki.Identity.Tests/`** (4)
- `Auditing/AuthEventLoggerTests.cs`
- `Controllers/MeControllerTests.cs`
- `RateLimiting/OAuthRateLimitedResponseTests.cs`
- `Validation/UserClassificationValidatorTests.cs`

**`tests/SDI.Enki.Infrastructure.Tests/`** (3)
- `Auditing/SystemCurrentUserTests.cs`
- `CalibrationProcessing/CalibrationProcessingServiceTests.cs`
- `SqlServer/ProvisioningRaceSmoke.cs` (today)

**`tests/SDI.Enki.Migrator.Tests/`** (3)
- `BootstrapEnvironmentCommandTests.cs`
- `IdentityBootstrapperTests.cs`
- `IdentityDbFixture.cs`

**`tests/SDI.Enki.WebApi.Tests/`** (12)
- `Authorization/TeamAuthHandlerTests.cs`
- `Background/TenantAuditRetentionServiceTests.cs`
- `Concurrency/ConcurrencyHelperTests.cs`
- `Controllers/AuditControllerCoverageTests.cs`
- `Controllers/CalibrationProcessingControllerTests.cs`
- `Controllers/CalibrationsControllerTests.cs`
- `Controllers/LicensesControllerTests.cs`
- `Controllers/MeControllerTests.cs`
- `Controllers/ToolsControllerCrudTests.cs`
- `Controllers/ToolsControllerTests.cs`
- `Fakes/FakeSurveyInterpolator.cs`
- `Multitenancy/TenantRoutingMiddlewareTests.cs`

The `TeamAuthHandlerTests.cs` carries the canonical 21-test matrix
covering every cell of the 8-step decision tree.

---

# 4. Top 15 commits by line count

| Rank | Commit | Subject | Insertions | Deletions |
|---:|---|---|---:|---:|
| 1 | `21e1273` | archived scripts to rebuild | +5,391 | 0 |
| 2 | `01206c2` | feat(authz): subtype + capability authorization with parametric policy | +4,174 | -861 |
| 3 | `0e812f3` | feat(users): edit profile + classify Team/Tenant + bind Tenant to one tenant | +3,608 | -56 |
| 4 | `4c624d0` | refactor(blazor): Pages/Wells/ → code-behind partials (20 files) | +2,945 | -2,775 |
| 5 | `ef37b24` | test: backfill coverage across Phases 1-4 + 6 (P5 deferred) | +2,742 | 0 |
| 6 | `82772b4` | feat(tools): structured retirement workflow with modal + audit columns | +2,222 | -115 |
| 7 | `2323850` | feat(migrator): SDI-ENG-PLAN-002 environment bootstrap via Migrator CLI | +2,192 | -501 |
| 8 | `af43fb7` | refactor(blazor): top-level Pages → code-behind partials (25 files) | +2,087 | -1,864 |
| 9 | `f130dda` | refactor(blazor): Pages/Runs/ → code-behind partials (12 files) | +2,013 | -1,925 |
| 10 | `b359ba2` | feat(runs): wire Tool / Calibration snapshot / Magnetics through Run | +1,917 | -2,026 |
| 11 | `bb4df5f` | refactor(models): rename InjectionWell → InterceptWell | +1,658 | -26 |
| 12 | `b4ff9d8` | feat(identity): per-user session lifetime override | +1,516 | -43 |
| 13 | `df95354` | feat(wells): MD-canonical depth model with survey-derived TVD | +1,345 | -235 |
| 14 | `545ae1d` | refactor(blazor): Pages/Admin/ → code-behind partials (10 files) | +1,133 | -1,053 |
| 15 | `01d983b` | docs(sop): add SDI-ENG-SOP-004 security testing protocol | +733 | 0 |

Top three are the most consequential to the application surface: the
authorization redesign, the user-management surface, and the test-and-
docs scaffolding around them.

---

# 5. Issues touched

20 distinct GitHub issue numbers referenced in commit subjects:

| Issue | Topic | Status by commit subject |
|---|---|---|
| #12 | (referenced in ArchDecisions) | n/a |
| #20 | RowVersion / ConcurrencyStamp pin | Closed (340bef6, e3ef3c3, 0480311) |
| #21 | Tenant cache after Provision | Closed (0f04d30) |
| #22 | Provision contract / 7807 content-type | Partial (f45fa1b) |
| #23 | Master-registry routing | Closed (4bf113e) |
| #24 | /connect/* 429 OAuth-JSON | Closed (1eed0e8) |
| #25 | Job Archive reversibility | Closed (8ae0da5) |
| #26 | Run / Tool / Calibration / Magnetics wiring | Closed (b359ba2) |
| #27 | SQL FK / unique → 409 translation | Closed (1be2115) |
| #28 | License download anchors | Closed (62ffd01) |
| #30 | Per-user session lifetime | Referenced (b4ff9d8) |
| #33 | Survey Import/Clear field-error surface | Closed (f6dad98) |
| #34 | Blazor overlay-stop async preventDefault | Closed (94b1842) |
| #36 | Calibration .mpf download | Closed (00bc08b) |
| #39 | Self-service password change | Closed (1d86235) |
| #41 | Tenant-select lands on Overview | Closed (066c624) |
| #42 | Calibration-compare Back button | Closed (55d6af7) |
| #43 | Calibration defaults validation at save | Closed (7ac725e) |
| #44 | Settings per-key reset + single-line inputs | Closed (41f2319) |
| #47 | Tools retirement workflow | Closed (82772b4) |

---

# 6. Outstanding work

Nothing in the period is a release-blocker for the demo. The tracked
backlog identifies three workstreams from `docs/plan-prototype-security.md`
(PLAN-001) that remain open before "first prototype customer ready":

| Workstream | Effort | Blocking? |
|---|---|---|
| **A** — Phase 5b auth UX (forgot-password + email confirmation) | 7–10 dev-days | Yes — admin-driven password resets don't scale |
| **B** — File-upload hardening (content-type, magic-byte, filename sanitisation) | 3–5 dev-days | Yes — exposes shot/log endpoints |
| **D** — Sensitive-op audit verification sweep | 0.5 dev-day | No — defensive |

Workstream C (secrets management) shipped in `ad22daa` + `3179b2a` and
is now Implemented.

Two minor side-observations from today's browser smoke that didn't
warrant their own session:

- `ProvisionTenant.razor` Blazor SSR form-post returns 503 (rather than
  200 with rendered error or clean 400) on the upstream-error path. UI
  renders correctly; wire status is misleading for monitoring.
- The `OptionalEmailAddressAttribute` could be applied to other DTOs
  with `[EmailAddress]` if the same paper-cut surfaces elsewhere.
  Today only the two tenant forms were verified.

---

# 7. Repository state at HEAD

| Field | Value |
|---|---|
| HEAD | `e5c2fc4` |
| Branch | `main` |
| Live URL | `https://dev.sdiamr.com/` |
| `.NET SDK` | 10.0.202 (per `global.json`) |
| Marduk pin | local sibling repo at `../Marduk/Marduk/` |
| Test pass rate | 712 of 712 (`dotnet test` last green: today) |

The customer-walkthrough SOP-004 is current as of today's audit pass
and covers 179 manual test rows runnable through any browser pointing
at `dev.sdiamr.com`.

---

*This document is generated from `git log` over the period stated.
The companion summary for the weekly meeting is in
[`2026-05-06-status-summary.md`](2026-05-06-status-summary.md).*

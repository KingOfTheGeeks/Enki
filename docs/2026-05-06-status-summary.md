---
title: "Enki — Weekly Status Summary"
subtitle: "Activity from 2026-04-30 noon through 2026-05-06"
author: "SDI · KingOfTheGeeks"
date: "2026-05-06"
---

# Enki — Weekly Status Summary

**Period covered:** Thursday 2026-04-30 12:00 — Wednesday 2026-05-06 17:36 (six and a half working days).
**Repo:** [`KingOfTheGeeks/Enki`](https://github.com/KingOfTheGeeks/Enki) on `main`.
**Live build:** `https://dev.sdiamr.com/` since 2026-05-05.

## Headline numbers

| Metric | Count |
|---|---:|
| Commits to `main` | **88** |
| Files modified or added (unique) | **445** |
| Net lines added across the repo | **+27,575** |
| GitHub issues referenced or closed | **20** |
| New automated tests | **+~110 (now 712 total)** |
| New test files | **26** |
| AI-paired commits | **81 of 88 (92%)** |
| Days with shipped commits | **7 of 7** |

## What landed

The week is dominated by **five shipped initiatives**, plus a long tail of bug-fixes and polish.

1. **Authorization redesign — fully shipped and live.** Replaced the previous ad-hoc role check with a parametric, two-axis (UserType × TeamSubtype) plus capability-claim model evaluated by a single `TeamAuthHandler` against thirteen named policies. Twelve are constructed from a single `TeamAuthRequirement` record; the thirteenth is `EnkiApiScope` as a default fallback. SOP-002 (Authorization Redesign) and SOP-003 (UI Gating) both promoted from Draft to Active today.

2. **Production deployment story complete.** New Migrator CLI command `bootstrap-environment` is now the canonical first-deploy path — applies Identity + Master + Tenant migrations, creates the OpenIddict client, creates the initial admin user, all idempotent. SDI-ENG-PLAN-002 (the plan documenting this) is in Implemented status. Required-secrets startup validation (PLAN-001 Workstream C) lands fail-loud on every host. IIS staging deployment is operating.

3. **Tenant-isolation hardening.** Cross-tenant data leakage is now impossible by construction (DB-per-tenant) and policy (the `TeamAuthHandler` enforces both type and membership gates). Six access-control gaps surfaced by an end-to-end audit (commit `bc24609`) are closed; defence-in-depth on the AdminUsers controller (`59a22d7`) closes the last bearer-token-isolation regression.

4. **Optimistic-concurrency contract — pinned and tested.** Issue #20 forced a fundamental fix: every `RowVersion` save now pins both `OriginalValue` and `CurrentValue` (not just one); ASP.NET Identity's `ConcurrencyStamp` is pinned the same way on admin user actions; lifecycle endpoints (Activate / Archive / Restore) carry the token via `LifecycleTransitionDto`. SOP-005 (Concurrency Validation) documents the engineering inventory; SOP-004 (the demo-walkable subset) carries the curated 13 tests for the staging walk.

5. **MD-canonical depth model.** Wells now treat Measured Depth as the canonical axis with TVD interpolated from surveys (commit `df95354`). Tubulars / Formations / Common Measures are bounded by Survey MD range and TVD is never directly entered. This brings Enki's depth model in line with industry practice and is the foundation for future anti-collision work.

## On top of that — long tail

A meaningful run of polish and small features:

- Self-service Change Password card on the account-settings page (issue #39).
- Per-user session-lifetime override (Mike at 1y, Gavin at 8h — exercises the override path).
- Tools retirement workflow with structured Disposition / Replacement / Reason (issue #47), with seeded retirement fixtures (1099001–1099006) covering each Disposition flavour.
- Calibration `.mpf` download from the CalibrationDetail page (issue #36); license-download anchors marked correctly so they land as files (#28).
- Job and License date fields treated as calendar dates rather than timestamps — fixes per-save day-drift (`5f901be`, `654656d`).
- Wells review punch-list P0..P3 fully cleared.
- `Injection` → `Intercept` renamed across every layer (entity / DTO / seed / docs / models) — `bb4df5f` is the structural rename, four other commits clean up consequences.

## Today (2026-05-06)

- Race-condition fix on `Tenants.Code` provisioning (`4e18192`) — translates SQL unique-violation to clean 400 ProblemDetails. Covered by deterministic `ProvisioningRaceSmoke` test.
- Empty Contact Email accepted on tenant Provision/Edit forms via new `OptionalEmailAddressAttribute` (`b3973c7`) — verified end-to-end on `dev.sdiamr.com`.
- `ArchDecision #12` documenting the Migrator three-channel output convention (`Console.WriteLine` / `Console.Error` / Serilog).
- **Repository-wide doc audit pass** (`e5c2fc4`): all 12 markdown docs verified against current code, five had factual drift fixed, three SOPs regenerated as `.docx` for client distribution.
- **Mass refactor** of every `.razor` page that still carried inline `@code` blocks into `.razor` + `.razor.cs` partial-class code-behind — 90 new `.razor.cs` files across ten commits, brings the BlazorServer host to a uniform shape.

## Demo readiness

System is operating at `dev.sdiamr.com`. All four hosts (Identity / WebApi / BlazorServer / Migrator) are healthy. The customer-walkthrough SOP-004 (179 manual test rows) is current and runnable end-to-end against the staging URL. The 14-persona seed roster is loaded; the three demo tenants (PERMIAN / NORTHSEA / BOREAL) are populated with full domain content.

**No release-blockers open.** The remaining items on the tracked backlog are deferred work (Phase 5b auth UX — TOTP MFA, email confirmation, password reset; file-upload hardening — magic-byte verification) covered by SDI-ENG-PLAN-001 and not gating the prototype-customer milestone.

## What's next

- **Phase 5b auth UX** (PLAN-001 Workstream A) is the headline outstanding work — 7–10 dev-days, depends on SMTP infrastructure landing first.
- **File-upload hardening** (PLAN-001 Workstream B) — 3–5 dev-days, parallel-able with A.
- **Audit-coverage verification sweep** (PLAN-001 Workstream D) — half a day, defensive only.

Estimated path to "first prototype customer ready": 10–14 dev-days serial, 8–10 days with the two engineers split across Workstreams A and B.

---

*This summary is generated from `git log` over the period stated. The companion document `docs/2026-05-06-status-detail.md` carries the per-area, per-commit breakdown.*

---
title: "Enki — Concurrency Test Plan"
subtitle: "Optimistic-concurrency surface, end to end"
author: "SDI · KingOfTheGeeks"
date: "2026-04-30"
---

# Enki — Concurrency Test Plan

This document is a standalone test plan for Enki's optimistic-concurrency
surface. It is independent of the system test plan
(`docs/test-plan.md`) — every concept it depends on is defined here, and
every test is callable without cross-referencing other documents.

The aim: **every operation in the system that mutates a row protected by
optimistic concurrency must surface a 409 conflict (with a reload-and-retry
banner) when a stale token is presented.** Field edits, lifecycle
transitions, admin user actions, tenant member operations, and the
cascading auto-recalc paths are all covered. Same-user and different-user
scenarios are tested explicitly because the user-side reproduction shape
differs even though the server-side mechanism is identical.

---

## A. Background — what concurrency means in Enki

Enki uses **optimistic concurrency** on every mutable, audited row. There
are two technical mechanisms — same idea, different storage:

| Mechanism | Where | Token type | Wire format |
|---|---|---|---|
| **`RowVersion`** | Tenant DB + Master DB on every `IAuditable` entity | SQL Server `rowversion` (8 bytes) | base64 string |
| **`ConcurrencyStamp`** | Identity DB on `ApplicationUser` | string GUID | string GUID |

Both work the same way:

1. The client GETs the row. The response includes the current token.
2. The client mutates locally and PUTs/POSTs back, **echoing the token it
   last saw**.
3. The server pins the supplied token as the EF entry's `OriginalValue`,
   then `SaveChanges`. EF generates `UPDATE … WHERE [token] = @client`.
4. If another writer moved the row forward in the meantime, the WHERE
   clause matches zero rows. EF raises `DbUpdateConcurrencyException`.
5. The controller's helper (`SaveOrConflictAsync` for RowVersion;
   `IdentityResultIsConcurrencyFailure` for ConcurrencyStamp) translates
   the failure into RFC 7807 **409 Conflict** with the message:
   *"The {entity} was modified by another user since you loaded it.
   Reload to see the latest values, then re-apply your edit."*

The user sees a red banner with that text. The form keeps their input
buffered locally — they are expected to reload, observe the other writer's
changes, decide whether to re-apply their own edit, and re-submit.

> **Same user vs different user:** the server-side mechanism is identical —
> two simultaneous saves produce the same 409 regardless of who's signed in.
> The reason both are tested below is that the **reproduction shape on the
> client** differs (one browser session vs two, one cookie vs two, what
> shows in the audit trail) and a regression on the user-resolution code
> path could mask the bug for one shape and not the other.

---

## B. Pre-conditions

Before running this plan, the following must be true:

1. **Fresh dev rig** at the build under test, launched per
   `scripts/start-dev.ps1 -Reset`. All three hosts (Identity / WebApi /
   BlazorServer) reporting *Now listening on …* with no startup errors.
2. **Two distinct accounts available**, one of:
   - For tenant-scoped surfaces: two tenant members of the same tenant
     (e.g., two seeded users who are both members of `PERMIAN`). At
     least one of them must hold the **Tenant Admin** role for tests
     touching tenant-Admin-gated operations.
   - For master / cross-tenant surfaces: two separate `enki-admin`
     accounts (e.g., `mike.king` plus another seeded admin).
3. **Two separate browser sessions**:
   - "Same user" tests use **two tabs in the same browser profile** —
     they share the auth cookie.
   - "Different user" tests use **one regular tab plus one incognito /
     private tab** (or two browsers, or two profiles) so that each tab
     carries a different authenticated cookie.
4. The seeded demo tenants (PERMIAN / NORTHSEA / BOREAL / CARNARVON) and
   their seed data (jobs, wells, surveys, runs, etc.) are present.

---

## C. Test conventions

Test rows look like this:

| ID | Test | Pass |
|---|---|---|
| CC-FE-JOB-01 | …steps and expected outcome… | [ ] |

| Field | Convention |
|---|---|
| ID | `CC-` (concurrency) + section prefix (`FE` field edits, `LC` lifecycle, `AU` admin users, `TM` tenant members, `CSC` cascading, `XU` cross-user, `REC` recovery, `EDGE` edge case, `GAP` known gap) + entity / scenario name + 2-digit number. |
| Test | Steps + expected. Read it as "do these steps; expect this." |
| Pass | `[ ]` empty → not run. `[x]` → passed. `[F]` → failed. |

**409 banner copy (for reference):**
*"The {entity} was modified by another user since you loaded it. Reload
to see the latest values, then re-apply your edit."*

Where the test description says *"…expect 409 banner"*, the operator
should observe a red alert with that copy on the Blazor page. For
lifecycle transitions, the banner appears on the entity's detail page
after a redirect with `?statusError=…`.

When a test fails, file a GitHub issue with the test ID in the title
(e.g., *"CC-FE-JOB-01: two-tab Job edit accepts stale RowVersion"*) and
include the request / response from the browser devtools Network tab.

---

## D. Smoke pass (10 minutes)

Run this first on every fresh build. If any of these fail, stop — every
specific test below is moot until smoke is green.

| ID | Test | Pass |
|---|---|---|
| CC-SMK-01 | Sign in as `mike.king`. Open any Job edit page in two tabs (same browser). Save Description=`A` in tab 1. Save Description=`B` in tab 2 → tab 2 shows the 409 banner. Reload tab 2 → page shows `A`. Edit to `C` and save → succeeds. | [ ] |
| CC-SMK-02 | Sign in as `mike.king`. Open Tenant detail for `BOREAL` in two tabs. Click Deactivate in tab 1 → tenant deactivates. Click Deactivate in tab 2 → 409 banner (NOT idempotent, because the row's RowVersion moved). Reload tab 2 → shows tenant is Inactive; no further action needed. | [ ] |
| CC-SMK-03 | Sign in as an `enki-admin`. Open any user's `/admin/users/{id}` detail page in two tabs. Click "Lock account" in tab 1 → user is locked. Click "Lock account" in tab 2 → 409 banner. Reload tab 2 → user shows as locked. | [ ] |
| CC-SMK-04 | Two distinct `enki-admin` users (regular tab + incognito). Both open the same Job edit page. User A saves Description=`X`. User B saves Description=`Y` → 409 banner in B. | [ ] |
| CC-SMK-05 | Sign in as `mike.king`. Edit a Job, save with the form's RowVersion field manually deleted via browser devtools → server returns 400 ValidationProblem with `rowVersion` field error. (Confirms the `[Required]` gate.) | [ ] |

Smoke green → continue.

---

## E. Field edits — `IAuditable` entities

Each row in the system that implements `IAuditable` carries a `RowVersion`
and is editable via an `Update*Dto` PUT/PATCH. The Blazor edit form
round-trips the RowVersion through a hidden `<InputText>` so the same
optimistic-concurrency contract applies to every field edit. Same-user
scenarios reproduce the bug class; different-user scenarios verify the
mechanism works regardless of who's writing.

### E.1 Tenant (master DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-TEN-01 | **Same user, two tabs.** Sign in as `enki-admin`. Open `/tenants/BOREAL/edit` in two tabs. In tab 1 change Display name to `BorealOne` and Save → success, redirect to detail. In tab 2 (still showing original Display name) change to `BorealTwo` and Save → **409 banner**. Reload tab 2 → shows `BorealOne`. | [ ] |
| CC-FE-TEN-02 | **Different users.** Two `enki-admin` accounts, two browser sessions. Both open `/tenants/BOREAL/edit`. Admin A saves Display name `Alpha` → success. Admin B saves Display name `Bravo` → 409 banner. | [ ] |
| CC-FE-TEN-03 | **Recovery.** From the 409 in CC-FE-TEN-01 or -02, reload tab 2. Verify the form now shows the post-A value. Edit again to `Final` and save → success. Reload → `Final` persists. | [ ] |
| CC-FE-TEN-04 | **No-change save (idempotent).** Sign in, open Tenant edit, click Save without changing anything → success (form re-submits with the same RowVersion that was loaded). Verify no spurious 409. | [ ] |

### E.2 Tenant Member (master DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-TM-01 | **Same user, two tabs.** Sign in as Tenant Admin of `PERMIAN`. Open `/tenants/PERMIAN/members` in two tabs. In tab 1 change a member's role from Contributor → Admin via the inline dropdown → success. In tab 2 (still showing Contributor) change the same member's role to Viewer → **409 banner**. Reload tab 2 → shows Admin. | [ ] |
| CC-FE-TM-02 | **Different Tenant Admins.** Two distinct Tenant Admins of `PERMIAN` (or one Tenant Admin + one `enki-admin`). Both open `/tenants/PERMIAN/members`. User A changes a role → success. User B changes the same row's role → 409 banner. | [ ] |
| CC-FE-TM-03 | **Recovery.** From the 409 above, reload tab 2 → role reflects A's choice. Re-apply B's intent → success. | [ ] |

### E.3 Tool (master DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-TOOL-01 | **Same user, two tabs.** Sign in as `enki-admin`. Open `/tools/{serial}/edit` for any Active tool in two tabs. In tab 1 change Display name and save → success. In tab 2 change Firmware version and save → **409 banner**. | [ ] |
| CC-FE-TOOL-02 | **Different users.** Two `enki-admin` accounts. Same scenario → 409 in second saver. | [ ] |
| CC-FE-TOOL-03 | **Recovery.** From CC-FE-TOOL-02's 409, reload, re-apply, save → success. | [ ] |

### E.4 Job (tenant DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-JOB-01 | **Same user, two tabs.** Sign in as a tenant member of `PERMIAN`. Open any Job edit page in two tabs. In tab 1 change Description to `A` and save → success. In tab 2 change Description to `B` and save → **409 banner**. Reload tab 2 → shows `A`. | [ ] |
| CC-FE-JOB-02 | **Different users (same tenant).** Two distinct members of `PERMIAN` (any role). Same scenario → 409 in second saver. | [ ] |
| CC-FE-JOB-03 | **Recovery.** Reload tab 2 → shows A's description. Edit to `C` → save succeeds. | [ ] |
| CC-FE-JOB-04 | **No-change save.** Open Job edit, click Save without changes → success, no 409. | [ ] |

### E.5 Well (tenant DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-WELL-01 | **Same user, two tabs.** Open `/tenants/PERMIAN/jobs/{id}/wells/{id}/edit` in two tabs. Save name change in tab 1 → success. Save different name in tab 2 → **409 banner**. | [ ] |
| CC-FE-WELL-02 | **Different users.** Same scenario across two sessions → 409. | [ ] |
| CC-FE-WELL-03 | **Recovery.** Reload, re-apply, save → success. | [ ] |

### E.6 Survey (tenant DB)

> **Special note.** Editing a Survey triggers per-well auto-recalc
> (`MardukSurveyAutoCalculator`) which writes back to **every other
> Survey on the well** — bumping each sibling's RowVersion. Tests in
> §H.1 "Cascading concurrency" cover this in addition to the basic
> two-tab case here.

| ID | Test | Pass |
|---|---|---|
| CC-FE-SUR-01 | **Same user, two tabs.** Open Surveys grid for a well in two tabs. Inline-edit Inclination on the same survey row in both — save tab 1 first → success. Save tab 2 → **409 banner**. | [ ] |
| CC-FE-SUR-02 | **Different users.** Same scenario across two sessions → 409. | [ ] |
| CC-FE-SUR-03 | **Recovery.** Reload tab 2 → grid shows tab 1's Inclination value. Re-apply if desired → success. | [ ] |

### E.7 Tie-On (tenant DB)

> **Special note.** A tie-on edit can prune surveys whose depth is now
> ≤ the new tie-on depth. Tests in §H.2 cover the prune cascade.

| ID | Test | Pass |
|---|---|---|
| CC-FE-TIE-01 | **Same user, two tabs.** Open the standalone tie-on edit page (`/tieons/{id}/edit`) or edit the tie-on row in the Surveys grid in two tabs. Save Depth change in tab 1 → success. Save in tab 2 → **409 banner**. | [ ] |
| CC-FE-TIE-02 | **Different users.** Same scenario → 409. | [ ] |
| CC-FE-TIE-03 | **Recovery.** Reload, re-apply → success. | [ ] |

### E.8 Tubular (tenant DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-TUB-01 | **Same user, two tabs.** Open the Tubulars grid in two tabs. Inline-edit a tubular's Diameter in both — save tab 1 → success. Save tab 2 → **409 banner**. | [ ] |
| CC-FE-TUB-02 | **Different users.** Same → 409. | [ ] |

### E.9 Formation (tenant DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-FRM-01 | **Same user, two tabs.** Edit a Formation's name in two tabs. Save tab 1 → success. Save tab 2 → **409 banner**. | [ ] |
| CC-FE-FRM-02 | **Different users.** Same → 409. | [ ] |

### E.10 Common Measure (tenant DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-CMM-01 | **Same user, two tabs.** Inline-edit a Common Measure's Value in two tabs. Save tab 1 → success. Save tab 2 → **409 banner**. | [ ] |
| CC-FE-CMM-02 | **Different users.** Same → 409. | [ ] |

### E.11 Magnetics (tenant DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-MAG-01 | **Same user, two tabs.** Open `/wells/{id}/magnetics/edit` in two tabs. Save Declination change in tab 1 → success. Save in tab 2 → **409 banner**. | [ ] |
| CC-FE-MAG-02 | **Different users.** Same → 409. | [ ] |
| CC-FE-MAG-03 | **Upsert path (no existing row).** On a well with no magnetics, both tabs are creating the row, not updating. Tab 1 creates → success. Tab 2 attempts to create → server treats as Update (because row now exists), tab 2's RowVersion is empty → **400 ValidationProblem** with `rowVersion` required. (Different from the 409 path; documents the upsert-create-then-update transition.) | [ ] |

### E.12 Run (tenant DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-RUN-01 | **Same user, two tabs.** Open Run edit page in two tabs. Save Description change in tab 1 → success. Save in tab 2 → **409 banner**. | [ ] |
| CC-FE-RUN-02 | **Different users.** Same → 409. | [ ] |

### E.13 Shot (tenant DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-SHOT-01 | **Same user, two tabs.** Open Shot edit page in two tabs. Save ShotName change in tab 1 → success. Save in tab 2 → **409 banner**. | [ ] |
| CC-FE-SHOT-02 | **Different users.** Same → 409. | [ ] |

### E.14 Log (tenant DB)

| ID | Test | Pass |
|---|---|---|
| CC-FE-LOG-01 | **Same user, two tabs.** Open Log edit page in two tabs. Save File-time change in tab 1 → success. Save in tab 2 → **409 banner**. | [ ] |
| CC-FE-LOG-02 | **Different users.** Same → 409. | [ ] |

---

## F. Lifecycle transitions

State-machine endpoints flip a `Status` column without taking a typical
request body. Each one now requires a `LifecycleTransitionDto` carrying
the caller's last-seen `RowVersion`. The Blazor pages emit a hidden
`<input name="rowVersion" value="@entity.RowVersion" />` in every
transition form; the MapPost proxy handlers in `BlazorServer/Program.cs`
forward the value to the WebApi as a JSON body.

> **Idempotent same-target case.** If both writers click the *same*
> transition (e.g., both click Archive), the second hit short-circuits
> at the *"already in target state"* check (`if (entity.Status == target)
> return NoContent();`) **before** the SaveChanges fires. So no 409 — the
> caller's intent was already satisfied by the first writer. This is
> correct behavior; tests that exercise this path expect 204 No Content.
> The 409 path requires a *different* target where the row's status
> has moved through the user's observed state.

### F.1 Job — Activate / Archive

| ID | Test | Pass |
|---|---|---|
| CC-LC-JOB-01 | **Same user, different targets.** Open a Draft Job's detail page in two tabs. In tab 1 click Activate → row goes to Active. In tab 2 (still showing Draft) click Archive → **409 banner** (Active → Archived is structurally valid but the stale RowVersion fires the conflict check). | [ ] |
| CC-LC-JOB-02 | **Different users, different targets.** Two members of the tenant. User A activates the Draft Job. User B clicks Archive while still seeing Draft → 409 banner. | [ ] |
| CC-LC-JOB-03 | **Same target — idempotent.** Both tabs (same user) click Archive on a Draft Job. Tab 1 → Job goes to Archived. Tab 2 → 204 No Content (no banner; the same-status short-circuit beats the SaveChanges check). Reload tab 2 → Archived state confirmed. | [ ] |
| CC-LC-JOB-04 | **Recovery.** From the 409 in CC-LC-JOB-01, reload tab 2 → shows Active. Click Archive (now with fresh RowVersion) → succeeds. | [ ] |

### F.2 Run — Start / Suspend / Complete / Cancel / Restore

| ID | Test | Pass |
|---|---|---|
| CC-LC-RUN-01 | **Start, same user.** Open a Planned Run's detail page in two tabs. Tab 1 clicks Start → Active. Tab 2 (still Planned) clicks Start → **204 idempotent** (same target). | [ ] |
| CC-LC-RUN-02 | **Suspend after Start.** Open a Planned Run in two tabs. Tab 1: Start → Active. Tab 2: Suspend → **409 banner** (Suspended is reachable from Active but RowVersion is stale). | [ ] |
| CC-LC-RUN-03 | **Complete after Start.** Open a Planned Run in two tabs. Tab 1: Start → Active. Tab 2: Complete → 409 banner. | [ ] |
| CC-LC-RUN-04 | **Cancel after Start.** Open a Planned Run in two tabs. Tab 1: Start → Active. Tab 2: Cancel → 409 banner. | [ ] |
| CC-LC-RUN-05 | **Restore (un-archive) different user.** Two tenant members. User A archives a Run (via Delete). User B then attempts Restore on a stale page state → 409 banner. (Restore takes a body with RowVersion; the Blazor page that surfaces this is the soft-archive admin view.) | [ ] |
| CC-LC-RUN-06 | **Recovery.** From any of the above 409s, reload tab 2 → shows the post-A state, allowed transitions update accordingly. Re-apply if still desired. | [ ] |

### F.3 Tenant — Deactivate / Reactivate

| ID | Test | Pass |
|---|---|---|
| CC-LC-TEN-01 | **Deactivate, different `enki-admin`s.** Two enki-admin sessions. Both open `/tenants/CARNARVON`. Admin A clicks Deactivate → success. Admin B clicks Deactivate → **409 banner**. | [ ] |
| CC-LC-TEN-02 | **Same user, two tabs, same target.** Both tabs open Active tenant. Tab 1 deactivates → success. Tab 2 (still showing Active, status pill outdated) clicks Deactivate → 409 banner (because the row moved to Inactive, RowVersion bumped, and the same-status short-circuit doesn't fire because the LOADED status was Active, not Inactive — the entity is loaded fresh per request and EF sees Inactive in DB; the SaveChanges fires with stale RowVersion). | [ ] |
| CC-LC-TEN-03 | **Reactivate from Inactive — different users.** On an Inactive tenant, two admins both click Reactivate. First succeeds; second → 409 banner. | [ ] |
| CC-LC-TEN-04 | **Recovery.** From CC-LC-TEN-01's 409 reload tab 2 → tenant shows Inactive. No further action needed (already in target state). | [ ] |

### F.4 Tool — Retire / Reactivate

| ID | Test | Pass |
|---|---|---|
| CC-LC-TOOL-01 | **Retire, same user.** Open an Active tool in two tabs. Tab 1: enter no reason, click Retire → tool retires. Tab 2 (still showing Active) clicks Retire → **204 idempotent** (same target Retired; same-status check). | [ ] |
| CC-LC-TOOL-02 | **Retire vs Reactivate race.** Open an Active tool in two tabs. Tab 1 retires it. Tab 2 (had Active) clicks… well, Active → Retired is the only same-target option here so this is hard to test naturally. (Tools have only Active ↔ Retired transitions exposed.) Skip if no different-target path is reachable. | [ ] |
| CC-LC-TOOL-03 | **Reactivate, different admins.** Tool is Retired. Two admins. A clicks Reactivate → success. B clicks Reactivate → 204 idempotent. | [ ] |
| CC-LC-TOOL-04 | **Retire with Reason, different users.** Two admins. A retires with reason `"end of life"`. B retires with reason `"shipped to lab"` → 204 idempotent (same target) — but **B's reason is silently dropped** because the same-status short-circuit fires. *Note this expected behavior; it is not a concurrency violation but a UX nuance: the second writer's reason isn't recorded.* | [ ] |

### F.5 Well — Restore (un-archive)

> No Blazor caller exists yet for `WellsController.Restore` — it is wired
> but no UI button posts to it. The endpoint nevertheless requires
> `LifecycleTransitionDto.RowVersion`. The test below uses curl / a REST
> client to verify the contract.

| ID | Test | Pass |
|---|---|---|
| CC-LC-WELL-01 | **API contract.** Soft-archive a well (DELETE). Then POST `/tenants/{code}/jobs/{jobId}/wells/{wellId}/restore` with no body → server returns **400 ValidationProblem** with `rowVersion` required. | [ ] |
| CC-LC-WELL-02 | **API contract — stale RowVersion.** Soft-archive a well. Capture its RowVersion via GET. Restore once with that RowVersion → 204. Restore *again* with the same (now-stale) RowVersion → 204 idempotent (already non-archived). Restore on a well that was modified by another writer between GET and POST → 409. | [ ] |

---

## G. Admin user actions — `ApplicationUser.ConcurrencyStamp`

ASP.NET Identity's `ConcurrencyStamp` is rotated on every save. The
admin-user surface (Lock / Unlock / SetAdminRole / ResetPassword) takes
the caller's last-seen stamp on the request body and pins it as the EF
`OriginalValue` so concurrent admins fighting over the same user surface
as 409 instead of last-writer-wins. The Blazor admin page reads the
stamp from the user's detail GET and posts it back on every action.

| ID | Test | Pass |
|---|---|---|
| CC-AU-01 | **Lock, same admin.** Sign in as `enki-admin`. Open `/admin/users/{id}` in two tabs. Tab 1: Lock → user is locked. Tab 2: Lock → **409 banner**. (NOT idempotent at the controller level — Lock always rotates the stamp via `SetLockoutEndDateAsync`, so the second hit's stale stamp triggers the failure.) | [ ] |
| CC-AU-02 | **Lock, different admins.** Two `enki-admin` sessions. Both open the same user. Admin A: Lock → success. Admin B: Lock → 409 banner. | [ ] |
| CC-AU-03 | **Unlock vs Lock race.** Open a Locked user in two admin tabs. Tab 1: Unlock → user is unlocked. Tab 2 (still showing Locked): Lock again → 409 banner. | [ ] |
| CC-AU-04 | **SetAdminRole — toggle race.** Open a non-admin user in two admin tabs. Tab 1: Grant admin role → user becomes admin. Tab 2 (still showing non-admin): Grant admin role → 409 banner. (Same-role short-circuits at the controller's `if (user.IsEnkiAdmin == desired) return NoContent();` ONLY after the stamp has been pinned — since the stamp moved, the request reaches the SaveChanges check before the same-role check returns; the actual EF flow throws ConcurrencyFailure first.) | [ ] |
| CC-AU-05 | **SetAdminRole — opposing intent.** User is non-admin. Tab 1: Grant admin → admin. Tab 2: also Grant admin → 204 NoContent (same desired target; the controller short-circuits BEFORE running UpdateAsync, so no stamp clash. The pinned stale stamp doesn't matter because no save fires). Reload tab 2 → user is admin. *Verify this is expected: same-target Grant produces 204, not 409.* | [ ] |
| CC-AU-06 | **ResetPassword race.** Open a user in two admin tabs. Tab 1: Reset password → success, temp password shown. Tab 2: Reset password → 409 banner (each reset rotates the security stamp). | [ ] |
| CC-AU-07 | **Self-protection beats stamp check.** Sign in as `enki-admin`. Try to lock your own account from `/admin/users/{your-own-id}` → server returns 409 "Self-lock disallowed" — NOT a concurrency-stamp 409. (Self-protection runs before the stamp pin in the controller's order.) | [ ] |
| CC-AU-08 | **Recovery.** From any 409 above, reload the user detail page → fresh stamp loaded. Re-apply the action → success. | [ ] |

---

## H. Cascading concurrency

Some operations don't just write the row the user is editing — they
trigger server-side recalculation that writes back to **sibling rows**.
Concurrency on those sibling rows is part of the contract.

### H.1 Survey auto-recalc

Editing one survey on a well triggers `MardukSurveyAutoCalculator.RecalculateAsync(db, wellId, ct)`
which recomputes the trajectory and writes back to **every other survey
on the same well**. Each affected sibling's RowVersion bumps. A second
user holding a stale view of any sibling will see 409 on their next save.

| ID | Test | Pass |
|---|---|---|
| CC-CSC-SUR-01 | **Auto-recalc cascade.** Open the Surveys grid in tab 1. Edit Survey #5's Inclination → save → succeeds (auto-recalc fires; computed columns on every survey update). In tab 2 (still showing the pre-recalc grid), inline-edit Survey #10's Depth → **409 banner** (Survey #10's RowVersion bumped during the recalc triggered by tab 1's edit, even though tab 2 was editing a different row). | [ ] |
| CC-CSC-SUR-02 | **Two users, two different survey edits, same well.** User A edits Survey #5 (commits, recalc fires). User B (had loaded the grid before A's commit) tries to edit Survey #10 → 409. **Verify the conflict message specifically says "Survey was modified" — even though B never touched Survey #5, B's edit is rejected because the auto-recalc cascade is a meaningful state change to B's view.** | [ ] |
| CC-CSC-SUR-03 | **Recovery.** After the 409, reload the grid in tab 2 → recalculated columns visible. Re-apply intent on Survey #10 → succeeds. | [ ] |

### H.2 Tie-on prune cascade

Editing the tie-on can promote/prune surveys (raising the tie-on depth
above existing surveys removes them).

| ID | Test | Pass |
|---|---|---|
| CC-CSC-TIE-01 | **Prune cascade.** Tab 1: change tie-on depth to a value above several surveys → those surveys are pruned. Tab 2 (had loaded the pre-prune grid), tries to edit one of the now-deleted surveys → server returns **404 Not Found** (the row no longer exists), NOT 409. Document this as expected — the soft-delete of survey rows is structural, not a concurrency conflict. | [ ] |
| CC-CSC-TIE-02 | **Tie-on race.** Tab 1: change tie-on Depth → success. Tab 2 (had loaded the original): change tie-on Inclination → **409 banner**. | [ ] |

---

## I. Tenant member operations

Tenant member adds, role changes, and removals all flow through
`TenantMembersController` and use RowVersion on `TenantUser` for
concurrency. The role-change inline dropdown on the Members page sends
the row's RowVersion as part of the PATCH body.

| ID | Test | Pass |
|---|---|---|
| CC-TM-01 | **Role change race, same admin.** Open the Members page for `PERMIAN` in two tabs. Tab 1: change Adam's role to Admin → success. Tab 2 (still showing Adam as Contributor): change his role to Viewer → **409 banner**. | [ ] |
| CC-TM-02 | **Role change race, different admins.** Two `enki-admin`s. Same scenario → 409 in second saver. | [ ] |
| CC-TM-03 | **Add vs Remove race.** Tab 1: Remove Zara from `PERMIAN` → success. Tab 2: change Zara's role → **404 Not Found** (membership row no longer exists; structural, not concurrency). | [ ] |
| CC-TM-04 | **Recovery from role-change 409.** Reload tab 2 → role reflects A's choice. Re-apply if desired → success. | [ ] |
| CC-TM-05 | **Add same user race.** Both tabs select the same candidate user from the "Add member" dropdown and submit. First succeeds; second → server returns **409 Conflict** with a duplicate-member problem detail (NOT a stale-RowVersion 409 — there's no row yet to have a version; the conflict is the unique `(TenantId, UserId)` index). | [ ] |

---

## J. Cross-user scenarios — sampler

Spot-checks across surfaces to confirm the same-mechanism, different-user
shape works regardless of who's signed in. Each test pair is run with
two distinct authenticated browser sessions (regular + incognito, or
two profiles).

| ID | Test | Pass |
|---|---|---|
| CC-XU-01 | Two tenant members. Both edit the same Job. Second saver → 409. | [ ] |
| CC-XU-02 | Two `enki-admin`s. Both edit the same Tenant. Second saver → 409. | [ ] |
| CC-XU-03 | Two `enki-admin`s. Both lock the same user. Second hitter → 409. | [ ] |
| CC-XU-04 | Two `enki-admin`s. Both deactivate the same Tenant. Second hitter → 409 (because the row moved to Inactive after A and B's stale RowVersion fails). | [ ] |
| CC-XU-05 | Tenant Admin + `enki-admin`. Both change the same member's role. Second saver → 409. | [ ] |
| CC-XU-06 | Two tenant members. Both edit different surveys on the same well. Second saver → 409 (auto-recalc cascade). | [ ] |
| CC-XU-07 | Audit trail check after CC-XU-01: open the Job's per-entity audit tile. Should show the first writer's `Updated` row only — the second writer's attempt produced no audit row because the save failed. | [ ] |

---

## K. Recovery flows

Verify that the Blazor UI guides the user through recovery after a
concurrency conflict.

| ID | Test | Pass |
|---|---|---|
| CC-REC-01 | **Edit-form recovery.** Hit a 409 on any field-edit (e.g., Job edit). Verify: red banner with the standard 409 copy; form keeps the user's typed values; `Save` button is still clickable. | [ ] |
| CC-REC-02 | **Reload after 409.** From the post-409 state in CC-REC-01, manually reload the page (browser refresh). Verify: form is freshly loaded with the post-A values; the user's previously-typed values are gone (no draft preservation across reloads); the RowVersion in the hidden field is fresh. | [ ] |
| CC-REC-03 | **Re-apply after reload.** Type the same change as before, save → success, redirect to detail page. | [ ] |
| CC-REC-04 | **Lifecycle 409 recovery.** Hit a 409 on a lifecycle transition (e.g., Job Archive after another tab Activated). Verify: detail page shows the post-A state with the right available transitions; banner explains the conflict; the now-allowed transitions reflect the new status. | [ ] |
| CC-REC-05 | **Audit trail for failed save.** Hit a 409 on any field edit. Open the per-entity audit tile. Verify: only the first (successful) writer's `Updated` row appears. The second writer's failed attempt is NOT in the audit log. | [ ] |

---

## L. Edge cases

Boundary conditions on the concurrency-token wire format and validation.

| ID | Test | Pass |
|---|---|---|
| CC-EDGE-01 | **Missing RowVersion.** Use browser devtools to delete the hidden `rowVersion` input from a Job edit form. Save → server returns **400 ValidationProblem** with `rowVersion` required. (Not a 409.) | [ ] |
| CC-EDGE-02 | **Empty RowVersion.** Same form, set the hidden input's value to `""`. Save → 400 ValidationProblem. | [ ] |
| CC-EDGE-03 | **Whitespace RowVersion.** Set the hidden input value to `"   "` → 400. | [ ] |
| CC-EDGE-04 | **Malformed base64 RowVersion.** Set the hidden input value to `"not-base64-!!!"` → server returns 400 ValidationProblem with "RowVersion must be a base64-encoded byte sequence." | [ ] |
| CC-EDGE-05 | **Truncated RowVersion.** Set the hidden input value to a valid base64 string of fewer than 8 bytes (e.g., `"AAAA"` decodes to 3 bytes). Save → server's WHERE clause matches no row → 409 banner. (NOT 400 — the format is technically valid base64.) | [ ] |
| CC-EDGE-06 | **RowVersion from a different entity.** Edit Job A. Replace the hidden RowVersion value with one captured from Job B (different row). Save → 409 (the value doesn't match Job A's row). | [ ] |
| CC-EDGE-07 | **Missing ConcurrencyStamp on admin user action.** Use devtools to strip the stamp from the JSON body of a `Lock` POST → server returns 400 ValidationProblem with `concurrencyStamp` required. | [ ] |
| CC-EDGE-08 | **Stamp from a different user.** POST `/admin/users/{A}/lock` with a body carrying user B's stamp → 409 banner (same shape as a stale stamp). | [ ] |
| CC-EDGE-09 | **No-change lifecycle.** Open a Draft Job. In the same tab, click Activate twice in rapid succession. Expected: first click → Active. Second click → 204 idempotent (same target). | [ ] |
| CC-EDGE-10 | **Stale RowVersion + invalid transition.** Open a Draft Job in two tabs. Tab 1: Archive → row Archived. Tab 2: Activate → check the order of failures. Per controller code, the order is: stamp pin → idempotent check → state-machine check → save. So Tab 2's stale RowVersion is pinned (no 400), idempotent check fails (target Active != current Archived), state-machine check fails (Archived → Active is invalid) → 409 with "Cannot transition" message. *Document the actual order observed if it differs.* | [ ] |

---

## M. Known gaps

Concurrency-shaped paths NOT covered by the current implementation.
These tests verify the gap exists (so a future fix has a regression
backstop) but currently expect the wrong-but-known behavior.

### M.1 Calibration current → superseded race

`CalibrationProcessingService.SaveAsync` loads the prior current
calibration, sets `prior.IsSuperseded = true`, adds a new row with
`IsSuperseded = false`, and `SaveChangesAsync` — without a unique index
or RowVersion pin on the Tool. Two concurrent admins finishing the
calibration wizard for the same tool can both succeed, leaving the tool
with two `IsSuperseded = false` rows. Documented at issue #20 as a known
gap; no fix in the current build.

| ID | Test | Pass |
|---|---|---|
| CC-GAP-CAL-01 | **Two admins racing the wizard.** Two `enki-admin` sessions. Both open `/tools/{serial}/calibrate` for the same tool. Both upload 25 valid `.bin` files, run through Compute, and click Save **simultaneously** (same wall-clock minute, ideally <1s apart). Expected (current bug): both saves succeed; the tool's calibrations grid now shows TWO calibrations both with the "Current" pill and neither with "Superseded". *This is the bug — once fixed, this test will produce one Current + one with a 409.* | [ ] |
| CC-GAP-CAL-02 | **Verification query.** After CC-GAP-CAL-01, run a SQL query: `SELECT COUNT(*) FROM master.Calibrations WHERE ToolId = ? AND IsSuperseded = 0` on the tool used. Currently expected count: **2**. After fix: 1. | [ ] |

---

## N. Performance / load (non-blocking)

Optional under-load checks. Run only when the build is otherwise green
on §D–§M.

| ID | Test | Pass |
|---|---|---|
| CC-LOAD-01 | **High concurrency on a single Job.** Use a load-generator (`hey`, `bombardier`, etc.) to PUT 50 concurrent updates to the same Job with random RowVersions. Expected: exactly one PUT succeeds (whichever happens to land first); the other 49 return 409. No deadlocks, no 500s. | [ ] |
| CC-LOAD-02 | **Survey auto-recalc under load.** 10 concurrent edits to different surveys on the same well. Expected: serialized at the SQL Server level via the rowversion check; some will 409, those that succeed proceed in order. No deadlocks. | [ ] |

---

## 99. Glossary

| Term | Definition |
|---|---|
| `RowVersion` | SQL Server `rowversion` column (8 bytes, auto-incremented on every UPDATE). EF Core configures it via `IsRowVersion()` so the column appears in the WHERE clause of UPDATE/DELETE statements. The wire format is base64. |
| `ConcurrencyStamp` | ASP.NET Identity's optimistic-concurrency token on `IdentityUser`. A string GUID rotated on every `UserManager.UpdateAsync`. |
| `OriginalValue` | EF's per-property memory of "what the database had when the entity was loaded." For concurrency tokens, this is the value EF compares in the UPDATE's WHERE clause. The pre-fix code only set CurrentValue, leaving OriginalValue at the loaded-from-DB value — which silently no-op'd the concurrency check. |
| `ApplyClientRowVersion` | Helper in `SDI.Enki.WebApi.Concurrency.ConcurrencyHelper` that pins both OriginalValue and CurrentValue to the client-supplied RowVersion bytes. |
| `ApplyClientConcurrencyStamp` | Identity-host equivalent for `ApplicationUser.ConcurrencyStamp`. Lives in `SDI.Enki.Identity.Concurrency.IdentityConcurrencyHelper`. |
| `SaveOrConflictAsync` | Helper that wraps `SaveChangesAsync` and translates `DbUpdateConcurrencyException` into a 409 ProblemDetails with the standard reload-and-retry copy. |
| `LifecycleTransitionDto` | Shared body shape for state-machine endpoints carrying `[Required] RowVersion`. |
| `IdentityResult.ConcurrencyFailure` | The error code ASP.NET Identity returns when its UPDATE catches a stamp mismatch. The Identity controller translates this into a 409 ProblemDetails. |
| 409 banner | The red alert with copy *"The {entity} was modified by another user since you loaded it. Reload to see the latest values, then re-apply your edit."* surfaced by the Blazor pages on a 409 response. |

---

*If anything in this plan is wrong, out-of-date, or unclear, file an
issue with the test ID. The plan is a living artefact — it should keep
up with the system it describes.*

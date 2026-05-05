---
title: "Enki — Authorization & Concurrency Validation (Staging UI)"
subtitle: "Test Protocol (Standard Operating Procedure)"
author: "SDI · KingOfTheGeeks"
date: "2026-05-04"
---

# Enki — Authorization & Concurrency Validation (Staging UI)

**Test Protocol (Standard Operating Procedure)**

| Field | Value |
| --- | --- |
| Document number | SDI-ENG-SOP-004 |
| Document type | Test Protocol |
| Version | 2.1 |
| Status | Active |
| Effective date | 2026-05-04 (v2.0); 2026-05-05 (v2.1) |
| Document owner | Mike King |
| Issuing organization | SDI Engineering |
| Standard alignment | IEEE 829 (Test Documentation), ISO 9001 §8 (Operation) |
| Related repository | <https://github.com/KingOfTheGeeks/Enki> |
| Related documents | SDI-ENG-SOP-002 (Authorization Redesign), SDI-ENG-SOP-003 (UI Gating), SDI-ENG-SOP-005 (Concurrency Validation — Engineering), `docs/Enki-Permissions-Matrix.docx` |

**Approval signatures**

| Role | Name | Signature | Date |
| --- | --- | --- | --- |
| Document Owner | Mike King | _________________ | __________ |
| Engineering Lead | _________________ | _________________ | __________ |
| QA Reviewer | _________________ | _________________ | __________ |

---

# 1. Purpose

This Test Protocol establishes the manual procedure for verifying that
the Enki platform's authorization and concurrency contracts are
enforced correctly **as observed through a web browser against the
staging IIS deployment**.

The procedure is designed to be runnable by a tester with no source
code, no SQL access, and no IIS administrative access — just a browser
pointing at the staging URL and credentials for the seed personas. The
goal is objective evidence that:

- No persona can perform an action outside their declared privileges,
  and every persona can perform every action within them (§8).
- The optimistic-concurrency contract holds on the highest-traffic
  write surfaces — concurrent edits surface as a 409 banner with the
  reload-and-retry recovery flow, not as a silent last-writer-wins
  overwrite (§9).

# 2. Scope

## 2.1 In scope

- Every authorization-sensitive route on the staging Blazor UI
  (`https://dev.sdiamr.com/`).
- The 14 seed personas as deployed to staging.
- Cross-tenant isolation as observable through the browser.
- Tenant deactivation (hard 404) as observable through the browser.
- Optimistic-concurrency behaviour on the highest-traffic write
  surfaces — Jobs, lifecycle transitions, admin user actions, tenant
  member adds, Survey auto-recalc cascade — as a curated subset (§9).

## 2.2 Out of scope

- Anything not reachable through the browser: `curl`, `sqlcmd`, SQL
  Management Studio, source code, log files on the IIS host, files
  on the SQL host.
- The full concurrency test inventory (covered in
  **SDI-ENG-SOP-005 — Concurrency Validation (Engineering)**;
  exercised by the engineering team against the dev rig).
- Edge cases requiring browser devtools manipulation
  (covered in SOP-005 §L).
- Performance / load testing (SOP-005 §N).
- Penetration testing (SQLi / XSS / fuzzing) — separate protocol.
- Sibling systems (Marduk, Esagila, Nabu).
- Non-Enki applications co-hosted on the same IIS box (Artemis,
  Athena).

## 2.3 Assumptions

- The staging IIS deployment is up and the operator has confirmed
  health probes are green prior to the test run.
- The full seed database is loaded on staging — including the 14
  seed personas, the three demo tenants (PERMIAN / NORTHSEA /
  BOREAL), and their canonical Job / Well / Survey / Run / Shot /
  Log seed data.
- The staging app pools (`sdiamr_identity`, `sdiamr_webapi`,
  `sdiamr_blazor`) are running.

# 3. Roles and Responsibilities

| Role | Responsibility |
| --- | --- |
| **Test Operator** | Executes the procedure in §8 and §9 in order. Records Pass / Fail per row in §12. Captures screenshots for failures. Files GitHub issues against the repository for every failure, using the Test ID in the issue title. |
| **Engineering Lead** | Triages every recorded failure. Either delivers a fix and notifies the Test Operator to re-execute the failing row, or accepts the deviation and records it in §12.3. |
| **QA Reviewer** | Reviews completed §12 for procedural conformance. Confirms screenshots and issue references for every Fail. Signs §12.4 to release the run. |
| **Release Manager** | Verifies the §12.4 sign-off before allowing the build to ship. Holds final authority on accepted deviations. |

# 4. Definitions and Acronyms

| Term | Definition |
| --- | --- |
| **409 banner** | A red alert displayed by the Blazor UI on a 409 Conflict response. Standard copy: *"The {entity} was modified by another user since you loaded it. Reload to see the latest values, then re-apply your edit."* |
| **Authorization gate** | A code path that decides whether a caller may perform an action. In Enki, every gate resolves to a named *policy*. |
| **Bearer token** | The OAuth 2.0 access token issued by the Identity host and validated by the WebApi on every API call. Visible to the tester only via the symptoms it produces (sign-in, 401 redirects); the tester never inspects token contents. |
| **Capability claim** | An orthogonal grant on a user. Currently only `licensing` is in use. |
| **Circuit (Blazor)** | A single SignalR-backed Blazor Interactive Server session. May outlive a sign-out / sign-in cycle within one browser tab. |
| **Concurrency token** | The optimistic-concurrency value the server compares against on save. SQL `RowVersion` for tenant + master DB writes, `ConcurrencyStamp` for Identity writes. The tester observes this only through 409 banner symptoms. |
| **`enki-admin`** | The role claim materialized at sign-in from the `IsEnkiAdmin` column on `ApplicationUser`. Acts as a root bypass for all policies except `EnkiAdminOnly` and the deactivation 404. |
| **Hard revocation** | Tenant deactivation returns 404 to every caller, including administrators. |
| **Membership** | A grant giving a Team user access to one tenant. Created on the tenant's Members page. |
| **OIDC** | OpenID Connect. The protocol for sign-in (authorization code + PKCE flow). |
| **Persona** | A seed user with a known privilege profile. Enumerated in §7. |
| **Policy** | A named authorization gate. Thirteen are defined on the WebApi (`EnkiPolicies.cs`); two on the Identity host. |
| **TeamSubtype** | The Field / Office / Supervisor classification on Team users. |
| **Tenant-bound user** | A user with `UserType = Tenant`, hard-bound to a single tenant via the `tenant_id` claim. |
| **Test ID** | A stable identifier of the form `SEC-{n}-{nnn}` for §8 and `CC-{group}-{nn}` for §9. The CC IDs match the canonical engineering inventory in SOP-005. |

# 5. Acceptance Criteria

| Criterion | Definition |
| --- | --- |
| **C1 — Smoke pass** | Every row in §8.1 records Pass. A failure halts the procedure. |
| **C2 — Cross-tenant isolation** | Every row in §8.14 records Pass. **A single failure here is a release-blocker** regardless of severity classification — this is the highest-stakes contract in the system. |
| **C3 — Concurrency smoke** | Every row in §9.1 records Pass. A failure here means optimistic-concurrency is broken on at least one of the three storage tiers (tenant DB / master DB / Identity DB) and is treated as a release-blocker for any feature touching that tier. |
| **C4 — Comprehensive pass** | Every row in §8.2 through §8.16 and §9.2 through §9.7 records Pass, OR is recorded as an accepted deviation in §12.3 with a tracked backlog item. |
| **C5 — Evidence** | Every Fail row in §12.2 has an associated GitHub issue reference and a screenshot stored alongside the run record. |
| **C6 — Sign-off** | §12.4 is signed by the Test Operator and the QA Reviewer. |

The Release Manager (§3) verifies all six criteria before release sign-off.

# 6. Pre-conditions and Test Environment

## 6.1 Required environment

- A web browser (Chrome / Edge current).
- Network access to `https://dev.sdiamr.com/`.
- **Two browser session contexts**: one regular tab, one Incognito /
  Private tab (or two browser profiles). Tests in §9 require signing
  in as different personas in each context simultaneously.
- Credentials for the 14 seed personas. Default password for every
  persona is `Enki!dev1` unless the staging operator has rotated
  them; confirm the active password with the operator before
  starting.

> **Single-tester discipline.** Staging at `dev.sdiamr.com` is a
> shared environment but this protocol assumes **only one tester is
> running at a time**. Concurrent runs interfere with each other —
> a second tester locking accounts in §8.12, racing tenant-member
> adds in §9.5, or deactivating PERMIAN in §8.15 while another
> tester is mid-walk will produce false positives. Coordinate with
> the staging operator before starting; they should pause other
> testers or schedule runs back-to-back.

The tester does **not** need:

- Source code of the application.
- Access to the SQL Server hosting the staging databases.
- Administrative access to the IIS server.
- Any command-line tooling beyond a web browser.

## 6.2 Required staging state

| Verification | Method | Expected |
| --- | --- | --- |
| **Sign-in card** | Browser to `https://dev.sdiamr.com/` (signed out). | "Sign in to continue" card renders; no error banner. |
| **Admin sign-in** | Sign in as `mike.king`. | Lands on home; username displayed top-right. |
| **Demo tenants present** | As `mike.king`, navigate to `/tenants`. | 3 rows, all Active: BOREAL, NORTHSEA, PERMIAN. |
| **PERMIAN membership** | As `mike.king`, navigate to `/tenants/PERMIAN/members`. | Exactly 4 members: dapo.ajayi, douglas.ridgway, jamie.dorey, joel.harrison. |
| **NORTHSEA membership** | As `mike.king`, navigate to `/tenants/NORTHSEA/members`. | Exactly 3 members: james.powell, jamie.dorey, travis.solomon. |
| **BOREAL membership** | As `mike.king`, navigate to `/tenants/BOREAL/members`. | Exactly 3 members: jamie.dorey, john.borders, scott.brandel. |
| **PERMIAN seed data** | As `mike.king`, navigate to `/tenants/PERMIAN/jobs`. | At least the seeded Jobs `Crest-North-Pad` and `MC252-Relief` are listed. |

If any verification fails, halt the procedure and notify the
Engineering Lead. Do not proceed.

# 7. Test Personas

The procedure exercises the following 14 seed personas. The default
password for every persona is `Enki!dev1` unless the staging operator
has rotated it. The privilege profile of each persona is documented
here as the prediction; the procedure verifies every cell against this
prediction.

| # | Username | UserType | TeamSubtype | IsEnkiAdmin | Capability | Memberships |
| --- | --- | --- | --- | :-: | --- | --- |
| P01 | `mike.king` | Team | Office | ✓ | — | (admin bypass) |
| P02 | `gavin.helboe` | Team | Office | ✓ | — | (admin bypass) |
| P03 | `jamie.dorey` | Team | Supervisor | — | — | PERMIAN, NORTHSEA, BOREAL |
| P04 | `douglas.ridgway` | Team | Office | — | — | PERMIAN |
| P05 | `james.powell` | Team | Office | — | — | NORTHSEA |
| P06 | `joel.harrison` | Team | Office | — | `licensing` | PERMIAN |
| P07 | `dapo.ajayi` | Team | Field | — | — | PERMIAN |
| P08 | `travis.solomon` | Team | Field | — | — | NORTHSEA |
| P09 | `scott.brandel` | Team | Field | — | — | BOREAL |
| P10 | `john.borders` | Team | Field | — | — | BOREAL |
| P11 | `adam.karabasz` | Team | Field | — | — | (none — control case) |
| P12 | `permian.fieldops` | Tenant | — | — | — | (bound to PERMIAN) |
| P13 | `northsea.drilling` | Tenant | — | — | — | (bound to NORTHSEA) |
| P14 | `boreal.engineer` | Tenant | — | — | — | (bound to BOREAL) |

# 8. Authorization tests

## 8.0 Conventions

Every test row in §8 and §9 uses these record values:

- **☐** — Not yet executed.
- **☑** — Pass: observed result matched expected result.
- **☒** — Fail: observed result did not match expected result. The Test
  Operator records the Test ID, captures a screenshot, and files a
  GitHub issue with the Test ID in the title.

**Sign-in / Sign-out procedure.** Each persona transition uses the
same mechanic:

1. In the active browser tab, click `SIGN OUT` (top-right).
2. Click `SIGN IN`.
3. Enter the persona's username; enter the password.
4. Click `SIGN IN` to submit.

**Same-tab discipline.** Tests in §8.11 specifically require sign-in /
sign-out cycles to occur in the same browser tab. Closing the tab and
opening a new one masks the defect those tests are designed to detect.

**Two-context discipline.** Tests in §9 use **two distinct browser
contexts** (one regular tab, one Incognito tab, or two profiles) so
each carries an independent auth cookie. Where a test says "two tabs
same browser" the contexts share a cookie (same persona); where it
says "two browsers" or "different users", each context carries its own
persona's cookie.

\newpage

## 8.1 Smoke verification (mandatory)

A failure here halts the procedure and is a build-blocker.

The first three rows hit each host's `/health/live` endpoint — the
process-up probe — directly in a fresh browser tab. The body should
read `Healthy` and the status bar should show `200 OK` (use the
browser's devtools Network panel if the body is hidden behind a
plain-text renderer). If the Identity or WebApi subdomain is not
publicly reachable from the customer network, mark the row as a
deviation (per §11) rather than Fail and have the operator confirm
the host is up server-side; the indirect checks in SEC-8.1-004..009
will still catch a downed host.

| Test ID | Test | Expected | Result |
| --- | --- | --- | --- |
| SEC-8.1-001 | Blazor host live | Open `https://dev.sdiamr.com/health/live` | 200 OK; body `Healthy` | ☐ |
| SEC-8.1-002 | Identity host live | Open `https://dev-shamash.sdiamr.com/health/live` | 200 OK; body `Healthy` | ☐ |
| SEC-8.1-003 | WebApi host live | Open `https://dev-isimud.sdiamr.com/health/live` | 200 OK; body `Healthy` (deviation if WebApi is not customer-reachable — it's an internal subdomain on most deploys) | ☐ |
| SEC-8.1-004 | Sign-in card renders | Browser to `https://dev.sdiamr.com/` (signed out) | "Sign in to continue" card renders; no error banner | ☐ |
| SEC-8.1-005 | Admin sign-in succeeds | Sign in as `mike.king` | Lands on home; username top-right (this also indirectly verifies Identity is reachable, since OIDC sign-in redirects through it) | ☐ |
| SEC-8.1-006 | Demo tenants present | As `mike.king`, open `/tenants` | 3 rows: BOREAL, NORTHSEA, PERMIAN; all Active (this also indirectly verifies WebApi is reachable from the Blazor host, since the tenant list comes from a WebApi call) | ☐ |
| SEC-8.1-007 | PERMIAN membership | As `mike.king`, open `/tenants/PERMIAN/members` | 4 members: dapo.ajayi, douglas.ridgway, jamie.dorey, joel.harrison | ☐ |
| SEC-8.1-008 | NORTHSEA membership | As `mike.king`, open `/tenants/NORTHSEA/members` | 3 members: james.powell, jamie.dorey, travis.solomon | ☐ |
| SEC-8.1-009 | BOREAL membership | As `mike.king`, open `/tenants/BOREAL/members` | 3 members: jamie.dorey, john.borders, scott.brandel | ☐ |

\newpage

## 8.2 Authentication coverage

Every persona signs in successfully. Sign in and sign out for each row.

| Test ID | Persona | Result |
| --- | --- | --- |
| SEC-8.2-001 | mike.king | ☐ |
| SEC-8.2-002 | gavin.helboe | ☐ |
| SEC-8.2-003 | jamie.dorey | ☐ |
| SEC-8.2-004 | douglas.ridgway | ☐ |
| SEC-8.2-005 | james.powell | ☐ |
| SEC-8.2-006 | joel.harrison | ☐ |
| SEC-8.2-007 | dapo.ajayi | ☐ |
| SEC-8.2-008 | travis.solomon | ☐ |
| SEC-8.2-009 | scott.brandel | ☐ |
| SEC-8.2-010 | john.borders | ☐ |
| SEC-8.2-011 | adam.karabasz | ☐ |
| SEC-8.2-012 | permian.fieldops | ☐ |
| SEC-8.2-013 | northsea.drilling | ☐ |
| SEC-8.2-014 | boreal.engineer | ☐ |

For every row the expected result is identical: sign-in succeeds, the
home page renders, the username appears in the top-right.

\newpage

## 8.3 Sidebar group visibility per persona

The five sidebar groups are: **OVERVIEW · TENANTS · FLEET · LICENSING · SYSTEM**.
A persona's sidebar must contain the listed groups and no others.

| Test ID | Persona | Expected groups | Result |
| --- | --- | --- | --- |
| SEC-8.3-001 | mike.king | OVERVIEW · TENANTS · FLEET · LICENSING · **SYSTEM** | ☐ |
| SEC-8.3-002 | gavin.helboe | OVERVIEW · TENANTS · FLEET · LICENSING · **SYSTEM** | ☐ |
| SEC-8.3-003 | jamie.dorey | OVERVIEW · TENANTS · FLEET · **LICENSING** | ☐ |
| SEC-8.3-004 | douglas.ridgway | OVERVIEW · TENANTS · FLEET | ☐ |
| SEC-8.3-005 | joel.harrison | OVERVIEW · TENANTS · FLEET · **LICENSING** | ☐ |
| SEC-8.3-006 | dapo.ajayi | OVERVIEW · TENANTS · FLEET | ☐ |
| SEC-8.3-007 | adam.karabasz | OVERVIEW · TENANTS · FLEET | ☐ |
| SEC-8.3-008 | permian.fieldops | OVERVIEW · TENANTS · FLEET; **TENANTS group has no `All Tenants` link** | ☐ |

\newpage

## 8.4 Tenants list visibility

Verify that `/tenants` returns the correct row count for each persona.
Tenant-bound users do not see the cross-tenant index — verify the
sidebar omits `All Tenants` for them.

| Test ID | Persona | Expected count | Expected codes | Result |
| --- | --- | --- | --- | --- |
| SEC-8.4-001 | mike.king | 3 | BOREAL · NORTHSEA · PERMIAN | ☐ |
| SEC-8.4-002 | jamie.dorey | 3 | BOREAL · NORTHSEA · PERMIAN | ☐ |
| SEC-8.4-003 | douglas.ridgway | 1 | PERMIAN | ☐ |
| SEC-8.4-004 | james.powell | 1 | NORTHSEA | ☐ |
| SEC-8.4-005 | joel.harrison | 1 | PERMIAN | ☐ |
| SEC-8.4-006 | dapo.ajayi | 1 | PERMIAN | ☐ |
| SEC-8.4-007 | travis.solomon | 1 | NORTHSEA | ☐ |
| SEC-8.4-008 | scott.brandel | 1 | BOREAL | ☐ |
| SEC-8.4-009 | john.borders | 1 | BOREAL | ☐ |
| SEC-8.4-010 | adam.karabasz | 0 | (empty list) | ☐ |
| SEC-8.4-011 | permian.fieldops | n/a | `All Tenants` link absent from sidebar | ☐ |

\newpage

## 8.5 Per-tenant page-level access

Verify that direct navigation to `/tenants/{code}` produces one of the
following observed outcomes:

- **Open** — overview page renders; sidebar shows TENANTS group with the tenant code badge plus Overview/Jobs/Audit (Members for Supervisor+).
- **Forbidden / clean shell** — body shows "Forbidden"; sidebar shows TENANTS group **without** the tenant code badge and **without** Overview/Jobs/Audit children.

| Test ID | Persona | URL | Expected | Result |
| --- | --- | --- | --- | --- |
| SEC-8.5-001 | mike.king | `/tenants/PERMIAN` | Open | ☐ |
| SEC-8.5-002 | mike.king | `/tenants/NORTHSEA` | Open | ☐ |
| SEC-8.5-003 | mike.king | `/tenants/BOREAL` | Open | ☐ |
| SEC-8.5-004 | jamie.dorey | `/tenants/PERMIAN` | Open | ☐ |
| SEC-8.5-005 | jamie.dorey | `/tenants/NORTHSEA` | Open | ☐ |
| SEC-8.5-006 | jamie.dorey | `/tenants/BOREAL` | Open | ☐ |
| SEC-8.5-007 | douglas.ridgway | `/tenants/PERMIAN` | Open | ☐ |
| SEC-8.5-008 | douglas.ridgway | `/tenants/NORTHSEA` | Forbidden / clean shell | ☐ |
| SEC-8.5-009 | douglas.ridgway | `/tenants/BOREAL` | Forbidden / clean shell | ☐ |
| SEC-8.5-010 | dapo.ajayi | `/tenants/PERMIAN` | Open | ☐ |
| SEC-8.5-011 | dapo.ajayi | `/tenants/NORTHSEA` | Forbidden / clean shell | ☐ |
| SEC-8.5-012 | dapo.ajayi | `/tenants/BOREAL` | Forbidden / clean shell | ☐ |
| SEC-8.5-013 | adam.karabasz | `/tenants/PERMIAN` | Forbidden / clean shell | ☐ |
| SEC-8.5-014 | adam.karabasz | `/tenants/NORTHSEA` | Forbidden / clean shell | ☐ |
| SEC-8.5-015 | adam.karabasz | `/tenants/BOREAL` | Forbidden / clean shell | ☐ |
| SEC-8.5-016 | permian.fieldops | `/tenants/PERMIAN` | Open | ☐ |
| SEC-8.5-017 | permian.fieldops | `/tenants/NORTHSEA` | Forbidden / clean shell | ☐ |
| SEC-8.5-018 | permian.fieldops | `/tenants/BOREAL` | Forbidden / clean shell | ☐ |
| SEC-8.5-019 | northsea.drilling | `/tenants/NORTHSEA` | Open | ☐ |
| SEC-8.5-020 | northsea.drilling | `/tenants/PERMIAN` | Forbidden / clean shell | ☐ |
| SEC-8.5-021 | boreal.engineer | `/tenants/BOREAL` | Open | ☐ |
| SEC-8.5-022 | boreal.engineer | `/tenants/PERMIAN` | Forbidden / clean shell | ☐ |

> **Note (informational, not procedural).** "Forbidden / clean shell" means the sidebar does not lie about the user's access. A row that says Forbidden but shows the tenant scope (badge + Overview/Jobs/Audit) is a known regression mode (Bug A2 in commit `59a22d7`). Tests SEC-8.5-017 / 018 / 020 / 022 are the canonical detectors for that regression.

\newpage

## 8.6 Master-scope action button visibility

Verify each action button on master-scope pages is visible to the right
audience and hidden from the rest. A button visible to a user without
the required policy is a UI gating defect; a button hidden from a user
with the required policy is a functional regression.

| Test ID | Persona | Page | Button | Expected | Result |
| --- | --- | --- | --- | --- | --- |
| SEC-8.6-001 | mike.king | `/tenants` | `+ NEW TENANT` | Visible | ☐ |
| SEC-8.6-002 | jamie.dorey | `/tenants` | `+ NEW TENANT` | Visible | ☐ |
| SEC-8.6-003 | douglas.ridgway | `/tenants` | `+ NEW TENANT` | Hidden | ☐ |
| SEC-8.6-004 | dapo.ajayi | `/tenants` | `+ NEW TENANT` | Hidden | ☐ |
| SEC-8.6-005 | mike.king | `/tools` | `+ NEW TOOL` | Visible | ☐ |
| SEC-8.6-006 | jamie.dorey | `/tools` | `+ NEW TOOL` | Visible | ☐ |
| SEC-8.6-007 | douglas.ridgway | `/tools` | `+ NEW TOOL` | Hidden | ☐ |
| SEC-8.6-008 | dapo.ajayi | `/tools` | `+ NEW TOOL` | Hidden | ☐ |
| SEC-8.6-009 | permian.fieldops | `/tools` | `+ NEW TOOL` | Hidden | ☐ |
| SEC-8.6-010 | mike.king | `/tenants/PERMIAN` | `DEACTIVATE` | Visible | ☐ |
| SEC-8.6-011 | jamie.dorey | `/tenants/PERMIAN` | `DEACTIVATE` | Visible | ☐ |
| SEC-8.6-012 | douglas.ridgway | `/tenants/PERMIAN` | `DEACTIVATE` | Hidden | ☐ |
| SEC-8.6-013 | mike.king | `/tenants/PERMIAN` | `EDIT` | Visible | ☐ |
| SEC-8.6-014 | douglas.ridgway | `/tenants/PERMIAN` | `EDIT` | Visible | ☐ |
| SEC-8.6-015 | dapo.ajayi | `/tenants/PERMIAN` | `EDIT` | Hidden | ☐ |

\newpage

## 8.7 Tenant-content writes (Office floor)

Verify the Office-floor write gate for tenant content. The UI gates
the buttons on the highest-traffic pages (Jobs, Wells); the rest of
the tenant-data pages (Surveys, TieOns, Tubulars, Formations,
CommonMeasures, Magnetics) sit on **API backstop only** this release —
buttons render for any tenant member who reaches the page, and the
API returns 403 to a Field / Tenant-bound caller on submit. See
SOP-003 (UI Gating) §E.4 for the inventory of which pages are
UI-gated vs. backstop-only.

The split below tests both surfaces:

  - SEC-8.7-001..010 verify the **UI gating** on Jobs / Wells.
  - SEC-8.7-011..016 verify the **API backstop** on the other tenant-data pages.

| Test ID | Persona | Page | Button | Expected | Result |
| --- | --- | --- | --- | --- | --- |
| SEC-8.7-001 | mike.king | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Visible | ☐ |
| SEC-8.7-002 | jamie.dorey | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Visible | ☐ |
| SEC-8.7-003 | douglas.ridgway | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Visible | ☐ |
| SEC-8.7-004 | joel.harrison | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Visible | ☐ |
| SEC-8.7-005 | dapo.ajayi | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Hidden (UI gate) | ☐ |
| SEC-8.7-006 | permian.fieldops | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Hidden (UI gate) | ☐ |
| SEC-8.7-007 | mike.king | a Job's Wells page | `+ NEW WELL` | Visible | ☐ |
| SEC-8.7-008 | douglas.ridgway | a Job's Wells page | `+ NEW WELL` | Visible | ☐ |
| SEC-8.7-009 | dapo.ajayi | a Job's Wells page | `+ NEW WELL` | Hidden (UI gate) | ☐ |
| SEC-8.7-010 | permian.fieldops | a Job's Wells page | `+ NEW WELL` | Hidden (UI gate) | ☐ |
| SEC-8.7-011 | mike.king | a Well's Surveys page | inline-edit a Survey row, save | Save succeeds | ☐ |
| SEC-8.7-012 | dapo.ajayi | a Well's Surveys page | inline-edit a Survey row, save | API 403 banner (UI doesn't gate this surface; the API is the backstop) | ☐ |
| SEC-8.7-013 | mike.king | a Well's Tubulars page | `+ NEW TUBULAR`, submit | Save succeeds | ☐ |
| SEC-8.7-014 | dapo.ajayi | a Well's Tubulars page | `+ NEW TUBULAR`, submit | API 403 banner (button is visible but the API rejects the submit) | ☐ |
| SEC-8.7-015 | mike.king | a Well's Formations page | `+ NEW FORMATION`, submit | Save succeeds | ☐ |
| SEC-8.7-016 | dapo.ajayi | a Well's Formations page | `+ NEW FORMATION`, submit | API 403 banner (same backstop pattern) | ☐ |

\newpage

## 8.8 Rig-side writes (Field+Tenant floor)

Runs, Shots, and Logs are gated by the class-level `CanAccessTenant`
policy with no action-level write override. This is intentional: the
rig-side write path runs from Field operators and Tenant-bound users.
Verify that Field members and Tenant-bound users can write to Runs,
Shots, and Logs on tenants they belong to, and that non-members
cannot.

| Test ID | Persona | Page | Button | Expected | Result |
| --- | --- | --- | --- | --- | --- |
| SEC-8.8-001 | mike.king | `/tenants/PERMIAN/jobs/{any}/runs` | `+ NEW RUN` | Visible | ☐ |
| SEC-8.8-002 | jamie.dorey | `/tenants/PERMIAN/jobs/{any}/runs` | `+ NEW RUN` | Visible | ☐ |
| SEC-8.8-003 | douglas.ridgway | `/tenants/PERMIAN/jobs/{any}/runs` | `+ NEW RUN` | Visible | ☐ |
| SEC-8.8-004 | dapo.ajayi | `/tenants/PERMIAN/jobs/{any}/runs` | `+ NEW RUN` | Visible | ☐ |
| SEC-8.8-005 | permian.fieldops | `/tenants/PERMIAN/jobs/{any}/runs` | `+ NEW RUN` | Visible | ☐ |
| SEC-8.8-006 | adam.karabasz | `/tenants/PERMIAN/jobs/{any}/runs` | (page) | Forbidden / clean shell | ☐ |
| SEC-8.8-007 | northsea.drilling | `/tenants/PERMIAN/jobs/{any}/runs` | (page) | Forbidden / clean shell | ☐ |
| SEC-8.8-008 | dapo.ajayi | a PERMIAN Shot detail | binary-upload control | Visible | ☐ |
| SEC-8.8-009 | permian.fieldops | a PERMIAN Shot detail | binary-upload control | Visible | ☐ |
| SEC-8.8-010 | northsea.drilling | a PERMIAN Shot detail | (page) | Forbidden / clean shell | ☐ |
| SEC-8.8-011 | dapo.ajayi | a PERMIAN Run's Logs grid | `+ NEW LOG` | Visible | ☐ |
| SEC-8.8-012 | permian.fieldops | a PERMIAN Run's Logs grid | `+ NEW LOG` | Visible | ☐ |
| SEC-8.8-013 | dapo.ajayi | a PERMIAN Log detail | binary-upload control | Visible | ☐ |
| SEC-8.8-014 | northsea.drilling | a PERMIAN Run's Logs grid | (page) | Forbidden / clean shell | ☐ |

\newpage

## 8.9 Tenant member management (Supervisor floor)

Verify that the Members link in the sidebar, the MEMBERS button on
the tenant overview, and direct navigation to `/tenants/{code}/members`
are accessible only to admin or to Supervisor-or-above tenant members.

| Test ID | Persona | URL | Expected | Result |
| --- | --- | --- | --- | --- |
| SEC-8.9-001 | mike.king | `/tenants/PERMIAN` | `MEMBERS` button visible; sidebar `Members` link visible | ☐ |
| SEC-8.9-002 | jamie.dorey | `/tenants/PERMIAN` | `MEMBERS` button visible; sidebar `Members` link visible | ☐ |
| SEC-8.9-003 | jamie.dorey | `/tenants/NORTHSEA` | `MEMBERS` button visible | ☐ |
| SEC-8.9-004 | jamie.dorey | `/tenants/BOREAL` | `MEMBERS` button visible | ☐ |
| SEC-8.9-005 | douglas.ridgway | `/tenants/PERMIAN` | `MEMBERS` button hidden; sidebar `Members` link hidden | ☐ |
| SEC-8.9-006 | dapo.ajayi | `/tenants/PERMIAN` | `MEMBERS` button hidden; sidebar `Members` link hidden | ☐ |
| SEC-8.9-007 | douglas.ridgway | `/tenants/PERMIAN/members` (typed) | Redirected to `/forbidden?required=Supervisor&resource=Members+%2F+PERMIAN`; tailored "requires Supervisor" message | ☐ |
| SEC-8.9-008 | mike.king | `/tenants/PERMIAN/members` | Page renders; 4 members listed; `ADD MEMBER` and `REMOVE` controls visible | ☐ |

\newpage

## 8.10 Licensing (Supervisor floor OR `licensing` capability)

Verify that the Licenses page is reachable by admin, Supervisor+, and
holders of the `licensing` capability claim — and is denied to other
personas.

| Test ID | Persona | URL | Expected | Result |
| --- | --- | --- | --- | --- |
| SEC-8.10-001 | mike.king | `/licenses` | Page renders; `+ GENERATE LICENSE` button visible | ☐ |
| SEC-8.10-002 | jamie.dorey | `/licenses` | Page renders; `+ GENERATE LICENSE` button visible | ☐ |
| SEC-8.10-003 | joel.harrison | `/licenses` | Page renders; `+ GENERATE LICENSE` button visible | ☐ |
| SEC-8.10-004 | douglas.ridgway | `/licenses` | Redirect to `/forbidden` | ☐ |
| SEC-8.10-005 | dapo.ajayi | `/licenses` | Redirect to `/forbidden` | ☐ |
| SEC-8.10-006 | permian.fieldops | `/licenses` | Redirect to `/forbidden` | ☐ |

\newpage

## 8.11 Bearer-token isolation across same-tab user changes (regression test for prior defect Bug G)

The Blazor SignalR circuit can outlive a sign-out / sign-in cycle within
the same browser tab. The fix in `CircuitTokenCache.cs` invalidates the
cached access token when the cookie principal's `sub` changes. This
section verifies that fix has not regressed.

**Setup**: open a single browser tab. Begin signed out. Do not close
the tab between rows.

| Test ID | Step | Expected | Result |
| --- | --- | --- | --- |
| SEC-8.11-001 | Sign in as dapo.ajayi. Navigate to `/tenants`. | List shows 1 tenant (PERMIAN). | ☐ |
| SEC-8.11-002 | **Same tab.** Sign out. Sign in as mike.king. Navigate to `/admin/users`. | User grid renders with 14 users. (If "You don't have access to this resource" appears, Bug G has regressed.) | ☐ |
| SEC-8.11-003 | **Same tab.** Sign out. Sign in as joel.harrison. Navigate to `/licenses`. | Page renders; `+ GENERATE LICENSE` button visible. | ☐ |
| SEC-8.11-004 | **Same tab.** Sign out. Sign in as dapo.ajayi. Navigate to `/licenses`. | Redirect to `/forbidden`. (If the Licenses grid renders, Bug G has regressed — Joel's stale token is being used.) | ☐ |
| SEC-8.11-005 | **Same tab.** Sign out. Sign in as permian.fieldops. Navigate to `/tenants/PERMIAN`. | Tenant overview renders; sidebar shows tenant scope. | ☐ |
| SEC-8.11-006 | **Same tab.** Navigate to `/tenants/NORTHSEA`. | Body forbidden; sidebar clean — no NORTHSEA badge, no Overview/Jobs/Audit children. (If sidebar shows the NORTHSEA scope, Bug A2 has regressed.) | ☐ |

\newpage

## 8.12 Identity host admin endpoints

Verify the admin endpoints (`/admin/users/*`, `/admin/audit/*`)
reach the correct audience. This section is also the regression test
for the prior defect Bug D (multi-`[Authorize]` attribute scheme split,
fixed in commit `bc24609` and hardened in commit `59a22d7`).

| Test ID | Persona | URL | Expected | Result |
| --- | --- | --- | --- | --- |
| SEC-8.12-001 | mike.king | `/admin/users` | Grid with 14 users; mike.king and gavin.helboe show ADMIN badge; jamie.dorey shows Supervisor subtype | ☐ |
| SEC-8.12-002 | mike.king | `/admin/users/{any user id}` | Detail page; `RESET PASSWORD`, `LOCK ACCOUNT`, `GRANT ADMIN ROLE` buttons visible | ☐ |
| SEC-8.12-003 | mike.king | `/admin/audit/auth-events` | Auth events feed renders | ☐ |
| SEC-8.12-004 | mike.king | `/admin/audit/identity` | Identity audit feed renders (empty state acceptable on a fresh seed) | ☐ |
| SEC-8.12-005 | mike.king | `/admin/audit/master` | Master audit feed renders | ☐ |
| SEC-8.12-006 | mike.king | `/admin/settings` | System settings page renders | ☐ |
| SEC-8.12-007 | jamie.dorey | `/admin/users` | Redirect to `/forbidden` | ☐ |
| SEC-8.12-008 | douglas.ridgway | `/admin/users` | Redirect to `/forbidden` | ☐ |
| SEC-8.12-009 | dapo.ajayi | `/admin/users` | Redirect to `/forbidden` | ☐ |
| SEC-8.12-010 | permian.fieldops | `/admin/users` | Redirect to `/forbidden` | ☐ |

\newpage

## 8.13 Self-service endpoints

Verify that every signed-in user can manage their own preferences and
change their own password, regardless of role.

| Test ID | Persona | URL / Action | Expected | Result |
| --- | --- | --- | --- | --- |
| SEC-8.13-001 | mike.king | `/account/settings` | Page renders with PREFERRED UNIT SYSTEM dropdown and Change Password card | ☐ |
| SEC-8.13-002 | dapo.ajayi | `/account/settings` | Page renders | ☐ |
| SEC-8.13-003 | permian.fieldops | `/account/settings` | Page renders | ☐ |
| SEC-8.13-004 | adam.karabasz | `/account/settings` | Page renders (no membership required for self-service) | ☐ |
| SEC-8.13-005 | Any persona | Hover the username top-right | Underline + accent color appear; cursor is pointer | ☐ |
| SEC-8.13-006 | Any persona | Click the username top-right | Navigates to `/account/settings` | ☐ |
| SEC-8.13-007 | Any persona | Submit the Change Password form with valid current and new values | Success message; sessions on other devices forced to re-auth on next API call | ☐ |

\newpage

## 8.14 Cross-tenant isolation (release-blocker)

**Any failure in this section is a release-blocker.** Cross-tenant data
leakage is the highest-stakes defect class in Enki. Verify that no
persona can see data belonging to a tenant they have no membership in.

The seed populates each demo tenant with distinct domain content:
- PERMIAN — 8-well Wolfcamp pad (`Crest-North-Pad`) plus relief-well demo (`MC252-Relief`).
- NORTHSEA — 3-well parallel laterals (`Atlantic-26-7H`) plus Wytch Farm ERD (`Wytch-Farm-M-Series`).
- BOREAL — SAGD producer/injector pair (`Cold-Lake-Pad-7`).

If a foreign tenant's data leaks into another tenant's view, it will
be visible by name.

| Test ID | Persona | URL | Expected | Result |
| --- | --- | --- | --- | --- |
| SEC-8.14-001 | permian.fieldops | `/tenants/PERMIAN/jobs` | PERMIAN jobs only (`Crest-North-Pad`, `MC252-Relief`); no Brent or Cold Lake names | ☐ |
| SEC-8.14-002 | permian.fieldops | `/tenants/NORTHSEA` | Forbidden / clean shell; no NORTHSEA data leaked into the body | ☐ |
| SEC-8.14-003 | permian.fieldops | `/tenants/BOREAL` | Forbidden / clean shell | ☐ |
| SEC-8.14-004 | northsea.drilling | `/tenants/NORTHSEA/jobs` | NORTHSEA jobs only (`Atlantic-26-7H`, `Wytch-Farm-M-Series`) | ☐ |
| SEC-8.14-005 | northsea.drilling | `/tenants/PERMIAN` | Forbidden / clean shell | ☐ |
| SEC-8.14-006 | boreal.engineer | `/tenants/BOREAL/jobs` | BOREAL jobs only (`Cold-Lake-Pad-7`) | ☐ |
| SEC-8.14-007 | dapo.ajayi | `/tenants/NORTHSEA/jobs` | Forbidden / clean shell | ☐ |
| SEC-8.14-008 | travis.solomon | `/tenants/PERMIAN/jobs` | Forbidden / clean shell | ☐ |
| SEC-8.14-009 | scott.brandel | `/tenants/PERMIAN/jobs` | Forbidden / clean shell | ☐ |
| SEC-8.14-010 | douglas.ridgway | `/tenants/NORTHSEA/jobs` | Forbidden / clean shell | ☐ |
| SEC-8.14-011 | james.powell | `/tenants/PERMIAN/jobs` | Forbidden / clean shell | ☐ |

\newpage

## 8.15 Tenant deactivation (hard revocation)

When a tenant is deactivated, every caller — including admins — receives
404 on its routes until reactivation. Run this section last so the
remaining tests don't trip on a deactivated tenant.

| Test ID | Step | Expected | Result |
| --- | --- | --- | --- |
| SEC-8.15-001 | As mike.king, navigate to `/tenants/PERMIAN`; click `DEACTIVATE`; confirm | Tenant marked Inactive | ☐ |
| SEC-8.15-002 | As mike.king, navigate to `/tenants/PERMIAN/jobs` | 404 (admin does NOT bypass deactivation) | ☐ |
| SEC-8.15-003 | As dapo.ajayi, navigate to `/tenants/PERMIAN` | Tenant Not Found / sidebar omits PERMIAN scope | ☐ |
| SEC-8.15-004 | As permian.fieldops, navigate to `/tenants/PERMIAN` | Tenant Not Found | ☐ |
| SEC-8.15-005 | As mike.king, navigate to `/tenants/PERMIAN`; click `REACTIVATE` | Tenant marked Active | ☐ |
| SEC-8.15-006 | As dapo.ajayi, navigate to `/tenants/PERMIAN/jobs` | Page loads; access restored | ☐ |

\newpage

## 8.16 Denial UX (`/forbidden` page)

Verify every denial path lands on `/forbidden`, not on the catch-all
"Not Found" page. Verify the tailored message renders when the
redirecting page supplies the `required` and `resource` query
parameters.

| Test ID | Persona | URL | Expected | Result |
| --- | --- | --- | --- | --- |
| SEC-8.16-001 | dapo.ajayi | `/admin/users` | Lands on `/forbidden`; heading "Access denied"; body "Your role doesn't include this action." | ☐ |
| SEC-8.16-002 | douglas.ridgway | `/tenants/PERMIAN/members` | Lands on `/forbidden?required=Supervisor&resource=Members+%2F+PERMIAN`; body shows "**requires Supervisor**" and "Requested resource: Members / PERMIAN" | ☐ |
| SEC-8.16-003 | Any signed-in persona | `/forbidden` | Page contains `BACK HOME` and `SIGN-IN SCREEN` action buttons | ☐ |

\newpage

# 9. Concurrency tests

Curated browser-doable subset of the canonical concurrency test
inventory in **SDI-ENG-SOP-005 — Concurrency Validation
(Engineering)**. Each test ID below matches the canonical CC-* ID in
SOP-005, so a tester who finds something interesting can drill into
the engineering reference for related cases.

The aim of these tests: confirm that two concurrent writes against
the same row surface as a 409 banner with the reload-and-retry
recovery flow, not as a silent last-writer-wins overwrite. Coverage
spans the three storage tiers — tenant DB (`RowVersion`), master DB
(`RowVersion`), Identity DB (`ConcurrencyStamp`).

**409 banner copy (for reference):**
*"The {entity} was modified by another user since you loaded it.
Reload to see the latest values, then re-apply your edit."*

\newpage

## 9.1 Smoke pass (mandatory)

A failure here means optimistic-concurrency is broken on at least one
storage tier. Halt the procedure and notify the Engineering Lead.

| Test ID | Test | Result |
| --- | --- | --- |
| CC-SMK-01 | Sign in as `mike.king`. Open any Job edit page in two tabs (same browser). Save Description=`A` in tab 1. Save Description=`B` in tab 2 → tab 2 shows the 409 banner. Reload tab 2 → page shows `A`. Edit to `C` and save → succeeds. (Tenant DB RowVersion check.) | ☐ |
| CC-SMK-02 | Sign in as `mike.king`. Open Tenant detail for `BOREAL` in two tabs. Click Deactivate in tab 1 → tenant deactivates. Click Deactivate in tab 2 → 409 banner. Reload tab 2 → shows tenant is Inactive. (Master DB RowVersion check. Reactivate BOREAL after the test.) | ☐ |
| CC-SMK-03 | Sign in as `mike.king`. Open any user's `/admin/users/{id}` detail page in two tabs. Click "Lock account" in tab 1 → user is locked. Click "Lock account" in tab 2 → 409 banner. (Identity DB ConcurrencyStamp check. Unlock the user after the test.) | ☐ |

\newpage

## 9.2 Field edits — Job

| Test ID | Test | Result |
| --- | --- | --- |
| CC-FE-JOB-01 | **Same user, two tabs.** Sign in as a tenant member of `PERMIAN`. Open any Job edit page in two tabs. In tab 1 change Description to `A` and save → success. In tab 2 change Description to `B` and save → 409 banner. Reload tab 2 → shows `A`. | ☐ |
| CC-FE-JOB-02 | **Different users, same tenant.** Two distinct PERMIAN members in two browser sessions (one regular tab + one Incognito). Same scenario as CC-FE-JOB-01: User A saves first → success; User B saves with stale page → 409 banner. | ☐ |

\newpage

## 9.3 Lifecycle transitions — Job

State-machine endpoints (Activate / Archive / Restore) carry the
caller's last-seen RowVersion via a hidden form field. The state
check + RowVersion check together produce the right behaviour:
different-target races land as 409; same-target races short-circuit
as 204 idempotent.

| Test ID | Test | Result |
| --- | --- | --- |
| CC-LC-JOB-01 | **Different targets, two tabs.** Open a Draft Job's detail page in two tabs as `mike.king`. Tab 1: click Activate → Active. Tab 2 (still showing Draft): click Archive → 409 banner. Reload tab 2 → shows Active; allowed transitions update. | ☐ |
| CC-LC-JOB-03 | **Same target, idempotent.** Open a Draft Job in two tabs. Both tabs click Archive. Tab 1 → Job goes to Archived. Tab 2 → no banner; the same-status short-circuit returns 204 No Content. Reload tab 2 → Archived state confirmed. | ☐ |

\newpage

## 9.4 Admin user actions

ASP.NET Identity uses `ConcurrencyStamp` instead of RowVersion. Each
save rotates the stamp; concurrent admins fighting over the same user
surface as 409 instead of last-writer-wins.

| Test ID | Test | Result |
| --- | --- | --- |
| CC-AU-01 | **Lock, same admin.** Sign in as `mike.king`. Open any non-admin user's `/admin/users/{id}` page in two tabs. Tab 1: Lock → user locked. Tab 2: Lock → 409 banner. (Lock always rotates the stamp; the second hit's stale stamp triggers the failure.) Unlock the user after the test. | ☐ |
| CC-AU-02 | **Lock, different admins.** Both admin sessions (one regular tab + one Incognito), each signed in as a different `enki-admin` (e.g., mike.king and gavin.helboe). Both open the same user. Admin A: Lock → success. Admin B: Lock → 409 banner. Unlock the user after the test. | ☐ |

\newpage

## 9.5 Tenant member operations

| Test ID | Test | Result |
| --- | --- | --- |
| CC-FE-TM-01 | **Add same user race, same admin.** Sign in as `mike.king`. Open `/tenants/PERMIAN/members` in two tabs. In tab 1 add candidate user `adam.karabasz` → success, row appears. In tab 2 (still showing adam in the candidate dropdown) add adam → 409 Conflict (the unique `(TenantId, UserId)` index catches the race). Reload tab 2 → adam is in the member grid; remove him after the test to restore the seed state. | ☐ |

\newpage

## 9.6 Cascading concurrency — Survey auto-recalc

Editing one survey on a well triggers per-well auto-recalculation that
writes back to **every other survey on the same well**. Each affected
sibling's RowVersion bumps. A second user holding a stale view of any
sibling will see 409 on their next save.

| Test ID | Test | Result |
| --- | --- | --- |
| CC-CSC-SUR-01 | **Auto-recalc cascade.** Sign in as `mike.king` in two tabs. Tab 1: open the Surveys grid for any well. Tab 2: same. In tab 1, edit Survey #5's Inclination → save → succeeds (auto-recalc fires server-side). In tab 2 (still showing the pre-recalc grid), inline-edit Survey #10's Depth → 409 banner. (Survey #10's RowVersion bumped during the recalc triggered by tab 1's edit, even though tab 2 was editing a different row.) | ☐ |

\newpage

## 9.7 Recovery flows

Verify the Blazor UI guides the user through recovery after a
concurrency conflict.

| Test ID | Test | Result |
| --- | --- | --- |
| CC-REC-01 | **Edit-form recovery.** Hit a 409 on any field-edit (e.g., the Job edit from CC-FE-JOB-01). Verify: red banner with the standard 409 copy; form keeps the user's typed values; `Save` button is still clickable. | ☐ |
| CC-REC-04 | **Lifecycle 409 recovery.** Hit a 409 on a lifecycle transition (e.g., the Archive-after-Activate from CC-LC-JOB-01). Verify: detail page shows the post-A state with the right available transitions; banner explains the conflict; the now-allowed transitions reflect the new status. | ☐ |

\newpage

# 10. Traceability

Each test in §8 and §9 traces back to a source authorization or
concurrency rule. This matrix supports impact analysis when a policy
or a concurrency token shape changes.

| Section | Rule under test | Source of truth |
| --- | --- | --- |
| 8.1 | Staging health (browser-observable) | Operator runbook (`docs/deploy.md`) |
| 8.2 | OIDC auth-code flow | SOP-002 §I |
| 8.3 | Sidebar group visibility | SOP-003 §E.1 |
| 8.4 | `EnkiApiScope` (any signed-in) + admin filter | SOP-002 §I, `EnkiPolicies.EnkiApiScope` |
| 8.5 | `CanAccessTenant`; clean-shell rule | SOP-002 §I, SOP-003 §E.5 |
| 8.6 | `CanProvisionTenants`, `CanManageMasterTools`, `CanManageTenantLifecycle`, `CanWriteMasterContent` | SOP-002 §I |
| 8.7 | `CanWriteTenantContent` (UI-gated on Jobs/Wells) + API backstop on others | SOP-002 §I, SOP-003 §E.4 |
| 8.8 | `CanAccessTenant` (class-level on Runs / Shots / Logs) | SOP-002 §J.7 |
| 8.9 | `CanManageTenantMembers` | SOP-002 §I |
| 8.10 | `CanManageLicensing` (Supervisor OR `licensing` capability) | SOP-002 §F, §I |
| 8.11 | Bearer-token isolation across cookie principal change | Code: `CircuitTokenCache` |
| 8.12 | `EnkiAdmin`, `EnkiAdminOrOffice` (Identity host) | SOP-002 §G |
| 8.13 | `EnkiApiScope` (any signed-in) self-service | SOP-002 §I |
| 8.14 | Tenant routing + `CanAccessTenant` | SOP-002 §H |
| 8.15 | Hard revocation on deactivation | SOP-002 §J (deactivation contract) |
| 8.16 | Denial UX | SOP-003 §F |
| 9.1 | Concurrency tokens (RowVersion + ConcurrencyStamp) | SOP-005 §A |
| 9.2–9.3 | RowVersion on tenant DB | SOP-005 §E.4, §F.1 |
| 9.4 | ConcurrencyStamp on Identity DB | SOP-005 §G |
| 9.5 | Member uniqueness race | SOP-005 §I |
| 9.6 | Auto-recalc cascade | SOP-005 §H.1 |
| 9.7 | Recovery flows | SOP-005 §K |

# 11. Deviation Handling

A **deviation** is a recorded outcome other than ☑ or ☒. Examples:
the test could not be executed because of an environmental problem;
the expected outcome was reached by a different mechanism than the
procedure described; the persona was unavailable for testing.

When a deviation occurs:

1. The Test Operator records the Test ID, the deviation, and the
   substitute outcome (if any) in §12.3.
2. The Engineering Lead reviews the deviation. The deviation is either:
   - Accepted, with rationale recorded in §12.3, or
   - Rejected, in which case the row reverts to ☐ and the Test Operator
     re-executes when the blocking condition is removed.
3. A Fail row (☒) is **not** a deviation. Fail outcomes are recorded
   normally and tracked through the GitHub issue workflow.

# 12. Test Records

This section is completed in full by the Test Operator at the end of
the run, then by the QA Reviewer.

## 12.1 Build identification

| Field | Value |
| --- | --- |
| Build commit (SHA) | `_____________` |
| Branch | `_____________` |
| Staging URL | `_____________` |
| Test Operator | `_____________` |
| Run start (UTC) | `_____________` |
| Run end (UTC) | `_____________` |

## 12.2 Section pass/fail summary

| Section | Pass count | Fail count | Deviations | Section result |
| --- | :-: | :-: | :-: | --- |
| §8.1 Smoke | _/9 | _ | _ | ☐ |
| §8.2 Authentication | _/14 | _ | _ | ☐ |
| §8.3 Sidebar visibility | _/8 | _ | _ | ☐ |
| §8.4 Tenants list | _/11 | _ | _ | ☐ |
| §8.5 Per-tenant page-level | _/22 | _ | _ | ☐ |
| §8.6 Master-scope buttons | _/15 | _ | _ | ☐ |
| §8.7 Office-floor writes | _/16 | _ | _ | ☐ |
| §8.8 Field+Tenant rig writes | _/14 | _ | _ | ☐ |
| §8.9 Tenant member management | _/8 | _ | _ | ☐ |
| §8.10 Licensing | _/6 | _ | _ | ☐ |
| §8.11 Bearer-token isolation (Bug G regression) | _/6 | _ | _ | ☐ |
| §8.12 Identity host admin | _/10 | _ | _ | ☐ |
| §8.13 Self-service | _/7 | _ | _ | ☐ |
| **§8.14 Cross-tenant isolation (release-blocker)** | _/11 | _ | _ | ☐ |
| §8.15 Deactivation hard revocation | _/6 | _ | _ | ☐ |
| §8.16 Denial UX | _/3 | _ | _ | ☐ |
| **§9.1 Concurrency smoke (release-gating)** | _/3 | _ | _ | ☐ |
| §9.2 Field edits — Job | _/2 | _ | _ | ☐ |
| §9.3 Lifecycle — Job | _/2 | _ | _ | ☐ |
| §9.4 Admin user actions | _/2 | _ | _ | ☐ |
| §9.5 Tenant member operations | _/1 | _ | _ | ☐ |
| §9.6 Survey auto-recalc cascade | _/1 | _ | _ | ☐ |
| §9.7 Recovery flows | _/2 | _ | _ | ☐ |
| **Totals** | _/179 | _ | _ |  |

## 12.3 Failures and deviations

Record every ☒ Fail and every deviation (per §11) below. For each, list
the Test ID, the observed outcome, the GitHub issue reference, and any
attached screenshots.

| Test ID | Type (Fail / Deviation) | Observed outcome | Issue | Screenshot ref |
| --- | --- | --- | --- | --- |
| _________ | __________ | __________ | __________ | __________ |
| _________ | __________ | __________ | __________ | __________ |
| _________ | __________ | __________ | __________ | __________ |

## 12.4 Sign-off

By signing below, the Test Operator attests that they walked the
procedure in order against a clean staging deploy, recorded the
outcomes truthfully, and raised an issue against every Fail.

By countersigning, the QA Reviewer attests that they reviewed the
records in §12.2 and §12.3 against the procedure in §8 and §9 and
confirms the records are complete and consistent.

| Role | Name | Signature | Date |
| --- | --- | --- | --- |
| Test Operator | _________________ | _________________ | __________ |
| QA Reviewer | _________________ | _________________ | __________ |

# 13. Records Retention

Completed runs of this protocol are retained alongside the release they
validated. Storage and retention follows SDI's records-retention policy:

- The completed Markdown / DOCX of this protocol (with §12 filled in)
  is committed to the release branch as `docs/test-runs/{date}-{commit}-sec.md`.
- Screenshots referenced in §12.3 are committed to the same path under
  a `screenshots/` subdirectory.
- GitHub issues referenced in §12.3 follow the standard issue lifecycle.

The signed cover page (§0) and §12.4 sign-off page may additionally be
retained as PDFs under the same path.

# 14. Document Control

## 14.1 Revision history

| Version | Date | Author | Summary of changes |
| --- | --- | --- | --- |
| 1.0 | 2026-05-02 | Mike King | Initial issue. First protocol in the SDI test-protocol going-forward template family. Covers every persona × every gate as of the May 2026 access-control review (commits `bc24609`, `0b2bd4a`, `59a22d7`). Targeted the dev rig (localhost:5073). |
| 1.1 | 2026-05-04 | Mike King | §8.8 widened to cover Logs alongside Runs / Shots after commit `4f3fc26` moved `LogsController` writes to the class-level `CanAccessTenant` floor (SEC-8.8-011..014 added). §8.7 reworked: SEC-8.7-011..016 reframed as API-backstop tests for Surveys / Tubulars / Formations to match the actual UI shape documented in SOP-003 §E.4. §11.2 totals adjusted (159 → 163). §4 Definitions: policy count 12 → 13 to include `EnkiAdminOnly`. |
| 2.0 | 2026-05-04 | Mike King | **Major rewrite.** Pivoted from "dev rig" to "staging UI" — every reference to localhost / curl / sqlcmd / start-dev.ps1 / source tree removed. URLs now point at the staging Blazor host (`https://dev.sdiamr.com/`). New §9 introduces a curated concurrency-test subset (13 tests, browser-doable) for inclusion in the same staging walk; the comprehensive concurrency inventory moves to **SDI-ENG-SOP-005 (Concurrency Validation — Engineering)**. Old §9 (Traceability) and following sections renumber to §10–§14. Acceptance criteria add C3 (concurrency smoke) and renumber. §11.2 totals 163 → 176. |
| 2.1 | 2026-05-05 | Mike King | §6.1 adds a single-tester-discipline note for the shared `dev.sdiamr.com` environment (concurrent runs interfere with §8.12 / §8.15 / §9.5 destructive surfaces). §8.1 expands the smoke pass with explicit `/health/live` rows for all three hosts (SEC-8.1-001..003); existing rows renumber to SEC-8.1-004..009. §11.2 totals 176 → 179. |

## 14.2 Change-control protocol

1. Every code change that alters an authorization gate (policy added,
   policy renamed, controller `[Authorize]` shape changed, capability
   added) **requires** a corresponding update to the relevant test
   row(s) in this document, in the same pull request.
2. Every code change that alters the optimistic-concurrency contract
   on a write surface covered in §9 (a new entity adopting RowVersion,
   the cascade rules changing, etc.) requires a corresponding update
   to §9 in the same pull request, with the canonical test ID kept in
   sync with **SOP-005**.
3. Adding or removing a §8 or §9 subsection bumps the protocol minor
   version (1.x → 1.x+1). Renumbering §8 / §9 subsections, or changing
   the deployment target (e.g., dev rig → staging), bumps the major
   version.
4. Every protocol version is tagged in source control alongside the
   Enki release it covers, so the protocol used to validate a given
   release is retrievable by checking out that release tag.

## 14.3 Storage and distribution

The authoritative source of this protocol is the Markdown
(`docs/sop-security-testing.md`). The compiled `.docx`
(`docs/sop-security-testing.docx`) is regenerated from the source at
release time. Print copies are uncontrolled.

---

*End of protocol.*

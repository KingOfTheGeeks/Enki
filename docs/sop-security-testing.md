---
title: "Enki — Authorization & Permissions Validation"
subtitle: "Test Protocol (Standard Operating Procedure)"
author: "SDI · KingOfTheGeeks"
date: "2026-05-02"
---

# Enki — Authorization & Permissions Validation

**Test Protocol (Standard Operating Procedure)**

| Field | Value |
| --- | --- |
| Document number | SDI-ENG-SOP-004 |
| Document type | Test Protocol |
| Version | 1.0 |
| Status | Active |
| Effective date | 2026-05-02 |
| Document owner | Mike King |
| Issuing organization | SDI Engineering |
| Standard alignment | IEEE 829 (Test Documentation), ISO 9001 §8 (Operation) |
| Related repository | <https://github.com/KingOfTheGeeks/Enki> |
| Related documents | SDI-ENG-SOP-002 (Authorization Redesign), SDI-ENG-SOP-003 (UI Gating), `docs/Enki-Permissions-Matrix.docx` |

**Approval signatures**

| Role | Name | Signature | Date |
| --- | --- | --- | --- |
| Document Owner | Mike King | _________________ | __________ |
| Engineering Lead | _________________ | _________________ | __________ |
| QA Reviewer | _________________ | _________________ | __________ |

---

# 1. Purpose

This Test Protocol establishes the manual procedure for verifying that
the Enki platform's authorization model — defined in
SDI-ENG-SOP-002 — is enforced correctly across every supported
combination of user persona, action, and tenant scope.

Successful execution of this procedure produces objective evidence that
no persona can perform an action outside their declared privileges, and
that every persona can perform every action within them. The procedure
exercises both the user-interface gates (UI hides what the user cannot
do) and the API-side enforcement (the security backstop), and it
specifically covers the cross-tenant isolation contract that protects
customer data segregation.

This document is the going-forward template for SDI test protocols.
Future protocols of this class follow the same structure.

# 2. Scope

## 2.1 In scope

- Every authorization-sensitive route on the BlazorServer host
  (`http://localhost:5073`).
- Every authorization-sensitive endpoint on the WebApi host
  (`http://localhost:5107`).
- Every authorization-sensitive endpoint on the Identity host
  (`http://localhost:5196`).
- The 14 seed personas and 10 seed memberships shipped by
  `scripts/start-dev.ps1 -Reset`.
- The cross-tenant isolation contract enforced by
  `TenantRoutingMiddleware` and `TeamAuthHandler`.
- The deactivation hard-revocation contract.

## 2.2 Out of scope

- Penetration testing (SQLi / XSS / fuzzing) — covered separately.
- Sibling systems (Marduk, Esagila, Nabu).
- Production tenant data — this protocol runs only against the dev
  seed in a clean state.
- Performance / load testing.

## 2.3 Assumptions

- The build under test is a clean checkout of a tagged release or a
  named branch, identified by `git rev-parse HEAD` recorded in §11.1.
- The test environment is the dev rig described in §6.

# 3. Roles and Responsibilities

| Role | Responsibility |
| --- | --- |
| **Test Operator** | Executes the procedure in §8 in order. Records Pass / Fail per row in §11. Captures screenshots for failures. Files GitHub issues against the repository for every failure, using the Test ID in the issue title. |
| **Engineering Lead** | Triages every recorded failure. Either delivers a fix and notifies the Test Operator to re-execute the failing row, or accepts the deviation and records it in §11.3. |
| **QA Reviewer** | Reviews completed §11 for procedural conformance. Confirms screenshots and issue references for every Fail. Signs §11.4 to release the run. |
| **Release Manager** | Verifies the §11.4 sign-off before allowing the build to ship. Holds final authority on accepted deviations. |

# 4. Definitions and Acronyms

| Term | Definition |
| --- | --- |
| **Authorization gate** | A code path that decides whether a caller may perform an action. In Enki, every gate resolves to a named *policy*. |
| **Bearer token** | The OAuth 2.0 access token issued by the Identity host and validated by the WebApi on every API call. |
| **Capability claim** | An orthogonal grant on a user, stored as an `enki:capability` row in the Identity DB. Currently only `licensing` is in use. |
| **Circuit (Blazor)** | A single SignalR-backed Blazor Interactive Server session. May outlive a sign-out / sign-in cycle within one browser tab. |
| **CRUD** | Create / Read / Update / Delete — the four basic operation classes used to characterize an action's risk class. |
| **`enki-admin`** | The role claim materialized at sign-in from the `IsEnkiAdmin` column on `ApplicationUser`. Acts as a root bypass for all policies except `EnkiAdminOnly` and the deactivation 404. |
| **Hard revocation** | Tenant deactivation returns 404 to every caller, including administrators. |
| **Membership** | A row in the master DB's `TenantUser` table. Grants a Team user access to one tenant. |
| **OIDC** | OpenID Connect. The protocol for sign-in (authorization code + PKCE flow). |
| **Persona** | A seed user with a known privilege profile. Enumerated in §7. |
| **Policy** | A named authorization gate. Thirteen are defined on the WebApi (`EnkiPolicies.cs`); two on the Identity host (`Program.cs`). |
| **TeamSubtype** | The Field / Office / Supervisor classification on Team users. |
| **Tenant-bound user** | A user with `UserType = Tenant`, hard-bound to a single tenant via the `tenant_id` claim. |
| **Test ID** | A stable identifier of the form `SEC-{n}-{nnn}`, naming exactly one test in this protocol. |

# 5. Acceptance Criteria

| Criterion | Definition |
| --- | --- |
| **C1 — Smoke pass** | Every row in §8.1 records Pass. A failure halts the procedure. |
| **C2 — Cross-tenant isolation** | Every row in §8.14 records Pass. **A single failure here is a release-blocker** regardless of severity classification — this is the highest-stakes contract in the system. |
| **C3 — Comprehensive pass** | Every row in §8.2 through §8.16 records Pass, OR is recorded as an accepted deviation in §11.3 with a tracked backlog item. |
| **C4 — Evidence** | Every Fail row in §11.2 has an associated GitHub issue reference and a screenshot stored alongside the run record. |
| **C5 — Sign-off** | §11.4 is signed by the Test Operator and the QA Reviewer. |

The Release Manager (§3) verifies all five criteria before release sign-off.

# 6. Pre-conditions and Test Environment

## 6.1 Required environment

The test environment is the local dev rig:

- Operating system: Windows 10/11 with .NET 10 SDK installed.
- SQL Server 2019+ accessible at `localhost` with `sa` privileges.
- Browser: Chrome / Edge current, with developer tools available.
- Repository checked out at `D:/<user>/Workshop/Enki/` (or equivalent).
- The companion `Marduk` repository checked out at the sibling path
  expected by the project's `<ProjectReference>` paths.

## 6.2 Required seed state

| Verification | Method | Expected |
| --- | --- | --- |
| **Clean rig** | Run `scripts/start-dev.ps1 -Reset` from a PowerShell prompt. | Every Enki database dropped and recreated. All three hosts launched. |
| **Hosts up** | Check the console for each host. | Each reports *Now listening on …* with no errors. |
| **Identity** | `curl http://localhost:5196/health` | HTTP 200 |
| **WebApi** | `curl http://localhost:5107/health` | HTTP 200 |
| **BlazorServer** | `curl http://localhost:5073/` | HTTP 200 |
| **Demo tenants** | Sign in as `mike.king` (password `Enki!dev1`); navigate to `/tenants`. | 3 rows, all Active: BOREAL, NORTHSEA, PERMIAN |
| **Memberships** | Run the SQL query in §6.3. | Exactly 10 rows as listed. |

## 6.3 Membership verification query

```sql
SELECT u.Name + ' -> ' + t.Code AS Membership
FROM   TenantUser tu
JOIN   [User]     u ON tu.UserId   = u.Id
JOIN   Tenant     t ON tu.TenantId = t.Id
ORDER BY t.Code, u.Name
```

Expected output:

```
jamie.dorey      -> BOREAL
john.borders     -> BOREAL
scott.brandel    -> BOREAL
james.powell     -> NORTHSEA
jamie.dorey      -> NORTHSEA
travis.solomon   -> NORTHSEA
dapo.ajayi       -> PERMIAN
douglas.ridgway  -> PERMIAN
jamie.dorey      -> PERMIAN
joel.harrison    -> PERMIAN
```

If any verification in §6.2 or §6.3 fails, halt the procedure and file
a build-blocking issue against the seeder. Do not proceed.

# 7. Test Personas

The procedure exercises the following 14 seed personas. The password
for every persona is `Enki!dev1`. The privilege profile of each persona
is documented here as the prediction; the procedure verifies every cell
against this prediction.

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

# 8. Test Procedure

## 8.0 Conventions

Every test row in this section uses these record values:

- **☐** — Not yet executed.
- **☑** — Pass: observed result matched expected result.
- **☒** — Fail: observed result did not match expected result. The Test
  Operator records the Test ID, captures a screenshot, and files a
  GitHub issue with the Test ID in the title.

**Sign-in / Sign-out procedure.** Each persona transition uses the same
mechanic:

1. In the active browser tab, click `SIGN OUT` (top-right).
2. Click `SIGN IN`.
3. Enter the persona's username; enter the password `Enki!dev1`.
4. Click `SIGN IN` to submit.

**Same-tab discipline.** Tests in §8.11 specifically require sign-in / sign-out
cycles to occur in the same browser tab. Closing the tab and opening a
new one masks the defect those tests are designed to detect.

\newpage

## 8.1 Smoke verification (mandatory)

A failure here halts the procedure and is a build-blocker.

| Test ID | Test | Method | Expected | Result |
| --- | --- | --- | --- | --- |
| SEC-8.1-001 | Identity host responds | `curl http://localhost:5196/health` | HTTP 200 | ☐ |
| SEC-8.1-002 | WebApi host responds | `curl http://localhost:5107/health` | HTTP 200 | ☐ |
| SEC-8.1-003 | BlazorServer host responds | `curl http://localhost:5073/` | HTTP 200 | ☐ |
| SEC-8.1-004 | Admin sign-in succeeds | Sign in as `mike.king` | Lands on home; username displayed top-right | ☐ |
| SEC-8.1-005 | Demo tenants present | As `mike.king`, open `/tenants` | 3 rows: BOREAL, NORTHSEA, PERMIAN; all Active | ☐ |
| SEC-8.1-006 | Memberships present | As `mike.king`, open `/tenants/PERMIAN/members` | Exactly 4 members listed: dapo.ajayi, douglas.ridgway, jamie.dorey, joel.harrison | ☐ |

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

Verify that the buttons gating Jobs / Wells / Surveys / Tubulars /
Formations / CommonMeasures / Magnetics writes appear only to admin or
to Office+ tenant members.

| Test ID | Persona | Page | Button | Expected | Result |
| --- | --- | --- | --- | --- | --- |
| SEC-8.7-001 | mike.king | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Visible | ☐ |
| SEC-8.7-002 | jamie.dorey | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Visible | ☐ |
| SEC-8.7-003 | douglas.ridgway | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Visible | ☐ |
| SEC-8.7-004 | joel.harrison | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Visible | ☐ |
| SEC-8.7-005 | dapo.ajayi | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Hidden | ☐ |
| SEC-8.7-006 | permian.fieldops | `/tenants/PERMIAN/jobs` | `+ NEW JOB` | Hidden | ☐ |
| SEC-8.7-007 | mike.king | a Job's Wells page | `+ NEW WELL` | Visible | ☐ |
| SEC-8.7-008 | douglas.ridgway | a Job's Wells page | `+ NEW WELL` | Visible | ☐ |
| SEC-8.7-009 | dapo.ajayi | a Job's Wells page | `+ NEW WELL` | Hidden | ☐ |
| SEC-8.7-010 | permian.fieldops | a Job's Wells page | `+ NEW WELL` | Hidden | ☐ |
| SEC-8.7-011 | mike.king | a Well's Surveys page | survey-edit / `+ NEW SURVEY` controls | Visible | ☐ |
| SEC-8.7-012 | dapo.ajayi | a Well's Surveys page | survey-edit / `+ NEW SURVEY` controls | Hidden | ☐ |
| SEC-8.7-013 | mike.king | a Well's Tubulars page | `+ NEW TUBULAR` | Visible | ☐ |
| SEC-8.7-014 | dapo.ajayi | a Well's Tubulars page | `+ NEW TUBULAR` | Hidden | ☐ |
| SEC-8.7-015 | mike.king | a Well's Formations page | `+ NEW FORMATION` | Visible | ☐ |
| SEC-8.7-016 | dapo.ajayi | a Well's Formations page | `+ NEW FORMATION` | Hidden | ☐ |

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

Verify the Identity host's admin endpoints (`/admin/users/*`,
`/admin/audit/*`) are gated by the `EnkiAdmin` and `EnkiAdminOrOffice`
policies and reach the correct audience. This section is also the
regression test for the prior defect Bug D (multi-`[Authorize]`
attribute scheme split, fixed in commit `bc24609` and hardened in
commit `59a22d7`).

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

# 9. Traceability

Each test in §8 traces back to a source authorization rule. This
matrix supports impact analysis when a policy is added, removed, or
renamed.

| Section | Rule under test | Source of truth |
| --- | --- | --- |
| 8.1 | Environment health | `scripts/start-dev.ps1` |
| 8.2 | OIDC auth-code flow | `src/SDI.Enki.Identity/Program.cs`, `src/SDI.Enki.BlazorServer/Program.cs` (cookie + OIDC) |
| 8.3 | Sidebar group visibility | `src/SDI.Enki.BlazorServer/Components/Layout/NavMenu.razor` |
| 8.4 | `EnkiApiScope` (any signed-in) + admin filter | `src/SDI.Enki.WebApi/Controllers/TenantsController.cs:75-113` |
| 8.5 | `CanAccessTenant`; clean-shell rule | `src/SDI.Enki.WebApi/Authorization/TeamAuthRequirement.cs`; `NavMenu.razor` `_canAccessTenant` |
| 8.6 | `CanProvisionTenants`, `CanManageMasterTools`, `CanManageTenantLifecycle`, `CanWriteMasterContent` | `src/SDI.Enki.Shared/Authorization/EnkiPolicies.cs` |
| 8.7 | `CanWriteTenantContent`, `CanDeleteTenantContent` | `EnkiPolicies.cs`; `JobsController.cs`, `WellsController.cs`, etc. |
| 8.8 | `CanAccessTenant` (class-level on Runs/Shots) | `RunsController.cs:69`, `ShotsController.cs:51` |
| 8.9 | `CanManageTenantMembers` | `EnkiPolicies.cs`; `TenantMembersController.cs` |
| 8.10 | `CanManageLicensing` (Supervisor OR `licensing` capability) | `EnkiPolicies.cs`; `LicensesController.cs:27` |
| 8.11 | `CircuitTokenCache.GetAccessTokenAsync` sub-validation | `src/SDI.Enki.BlazorServer/Auth/CircuitTokenCache.cs`; unit tests in `tests/SDI.Enki.BlazorServer.Tests/Auth/CircuitTokenCacheTests.cs` |
| 8.12 | `EnkiAdmin`, `EnkiAdminOrOffice` (Identity host) | `src/SDI.Enki.Identity/Program.cs:201-230`; `AdminUsersController.cs` |
| 8.13 | `EnkiApiScope` (any signed-in) | `src/SDI.Enki.Identity/Controllers/MeController.cs` |
| 8.14 | Tenant routing + `CanAccessTenant` | `TenantRoutingMiddleware.cs`; `TeamAuthHandler` step 4 |
| 8.15 | Hard revocation on deactivation | `TenantRoutingMiddleware.cs:78` |
| 8.16 | Cookie `AccessDeniedPath` + `Forbidden.razor` | `BlazorServer/Program.cs`; `Components/Pages/Forbidden.razor` |

# 10. Deviation Handling

A **deviation** is a recorded outcome other than ☑ or ☒. Examples:
the test could not be executed because of an environmental problem; the
expected outcome was reached by a different mechanism than the procedure
described; the persona was unavailable for testing.

When a deviation occurs:

1. The Test Operator records the Test ID, the deviation, and the
   substitute outcome (if any) in §11.3.
2. The Engineering Lead reviews the deviation. The deviation is either:
   - Accepted, with rationale recorded in §11.3, or
   - Rejected, in which case the row reverts to ☐ and the Test Operator
     re-executes when the blocking condition is removed.
3. A Fail row (☒) is **not** a deviation. Fail outcomes are recorded
   normally and tracked through the GitHub issue workflow.

# 11. Test Records

This section is completed in full by the Test Operator at the end of
the run, then by the QA Reviewer.

## 11.1 Build identification

| Field | Value |
| --- | --- |
| Build commit (SHA) | `_____________` |
| Branch | `_____________` |
| Build timestamp | `_____________` |
| Test Operator | `_____________` |
| Run start (UTC) | `_____________` |
| Run end (UTC) | `_____________` |

## 11.2 Section pass/fail summary

| Section | Pass count | Fail count | Deviations | Section result |
| --- | :-: | :-: | :-: | --- |
| §8.1 Smoke | _/6 | _ | _ | ☐ |
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
| **Totals** | _/163 | _ | _ |  |

## 11.3 Failures and deviations

Record every ☒ Fail and every deviation (per §10) below. For each, list
the Test ID, the observed outcome, the GitHub issue reference, and any
attached screenshots.

| Test ID | Type (Fail / Deviation) | Observed outcome | Issue | Screenshot ref |
| --- | --- | --- | --- | --- |
| _________ | __________ | __________ | __________ | __________ |
| _________ | __________ | __________ | __________ | __________ |
| _________ | __________ | __________ | __________ | __________ |

## 11.4 Sign-off

By signing below, the Test Operator attests that they walked the
procedure in order against a clean seed, recorded the outcomes
truthfully, and raised an issue against every Fail.

By countersigning, the QA Reviewer attests that they reviewed the
records in §11.2 and §11.3 against the procedure in §8 and confirms the
records are complete and consistent.

| Role | Name | Signature | Date |
| --- | --- | --- | --- |
| Test Operator | _________________ | _________________ | __________ |
| QA Reviewer | _________________ | _________________ | __________ |

# 12. Records Retention

Completed runs of this protocol are retained alongside the release they
validated. Storage and retention follows SDI's records-retention policy:

- The completed Markdown / DOCX of this protocol (with §11 filled in)
  is committed to the release branch as `docs/test-runs/{date}-{commit}-sec.md`.
- Screenshots referenced in §11.3 are committed to the same path under
  a `screenshots/` subdirectory.
- GitHub issues referenced in §11.3 follow the standard issue lifecycle.

The signed cover page (§0) and §11.4 sign-off page may additionally be
retained as PDFs under the same path.

# 13. Document Control

## 13.1 Revision history

| Version | Date | Author | Summary of changes |
| --- | --- | --- | --- |
| 1.0 | 2026-05-02 | Mike King | Initial issue. First protocol in the SDI test-protocol going-forward template family. Covers every persona × every gate as of the May 2026 access-control review (commits `bc24609`, `0b2bd4a`, `59a22d7`). |

## 13.2 Change-control protocol

1. Every code change that alters an authorization gate (policy added,
   policy renamed, controller `[Authorize]` shape changed, capability
   added) **requires** a corresponding update to the relevant test
   row(s) in this document, in the same pull request.
2. Adding or removing a §8 subsection bumps the protocol minor
   version (1.x → 1.x+1). Renumbering §8 subsections bumps the
   major version.
3. Every protocol version is tagged in source control alongside the
   Enki release it covers, so the protocol used to validate a given
   release is retrievable by checking out that release tag.

## 13.3 Storage and distribution

The authoritative source of this protocol is the Markdown
(`docs/sop-security-testing.md`). The compiled `.docx`
(`docs/sop-security-testing.docx`) is regenerated from the source at
release time. Print copies are uncontrolled.

---

*End of protocol.*

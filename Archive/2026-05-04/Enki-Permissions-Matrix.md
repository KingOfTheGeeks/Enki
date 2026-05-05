---
title: "Enki Access Control Matrix"
subtitle: "Per-entity CRUD permissions by team role"
author: "Generated from src/SDI.Enki.Shared/Authorization/EnkiPolicies.cs and the WebApi + Identity controllers"
date: "2026-05-02"
---

# 1. Overview

Authorization in Enki runs on two orthogonal axes plus an admin flag and capability claims.

**Axis A — `UserType` (column on `ApplicationUser`):**

- **Team** — SDI staff. Authenticate against the Identity host; reach master + tenant endpoints. Must carry a `TeamSubtype`.
- **Tenant** — customer-side users. Hard-bound to **one** tenant via `tenant_id` claim. Locked out of master endpoints; restricted to Field-equivalent operations within their bound tenant.

**Axis B — `TeamSubtype` (Team users only, SmartEnum):**

- **Field** (1) — downhole operators
- **Office** (2) — analysts / coordinators / support
- **Supervisor** (3) — field supervisors

The hierarchy is numeric — a check for "Office or above" passes for both Office and Supervisor.

**Flag — `IsEnkiAdmin`:** column on `ApplicationUser`, materialised at sign-in by `EnkiUserClaimsPrincipalFactory` into the `enki-admin` role claim. Acts as a **root bypass** for every policy except `EnkiAdminOnly` (which requires it explicitly) and the tenant-deactivation 404 (which is enforced at routing time).

**Capability claims** (`enki:capability` on `AspNetUserClaim`): orthogonal grants stored as user claims. Currently only `licensing` is in use — it lets a non-Supervisor user issue/revoke licenses.

# 2. Roles

| Role | What it grants on top of "any signed-in user" |
| --- | --- |
| **Field** | Tenant-scoped reads + Runs/Shots writes (when a member of the tenant in URL scope). |
| **Office** | Field + tenant-content writes (Jobs/Wells/Surveys/etc.) + master content writes (Calibrations, Tenant settings). |
| **Supervisor** | Office + tenant-member management + master Tools CRUD + tenant provisioning + tenant lifecycle + license issue/revoke + master roster reads. |
| **enki-admin** | Bypasses every policy except the tenant-deactivation 404 and the explicit `EnkiAdminOnly` gate. Can manage any user, flip admin role, grant/revoke capabilities, read identity audit. |
| **Tenant-bound user** | Field-equivalent operations within their bound tenant only. Denied master endpoints. Denied any policy with a `MinimumSubtype` set. |
| **Holds `licensing` capability** | Equivalent to Supervisor for `CanManageLicensing` only. Does NOT grant any other Supervisor-tier action. |

# 3. Policy decision tree

The single `TeamAuthHandler` evaluates every policy through this short-circuit tree (first matching rule wins):

1. If the policy declares `RequireAdmin = true` → succeed iff `IsEnkiAdmin`. Else deny.
2. If `IsEnkiAdmin` → succeed (root bypass).
3. If the principal's `user_type = Tenant`:
    - Deny if the policy is master-scoped.
    - Bind-check the `tenant_id` claim against the route's `{tenantCode}`.
    - Deny if the policy declares any `MinimumSubtype`.
    - Else succeed.
4. If the policy is tenant-scoped, require a `TenantUser` row for the (caller, tenant) pair. Deny on miss.
5. If no `MinimumSubtype` is set → succeed.
6. If the caller's `team_subtype` satisfies the floor → succeed.
7. If the caller carries the policy's `GrantingCapability` claim → succeed.
8. Else deny.

# 4. Policy summary

The 12 WebApi policies live in [`EnkiPolicies.cs`](../src/SDI.Enki.Shared/Authorization/EnkiPolicies.cs); the 2 Identity-host policies live in [`Identity Program.cs`](../src/SDI.Enki.Identity/Program.cs).

| Policy | Field | Office | Supervisor | Tenant-bound | enki-admin | Capability |
| --- | :---: | :---: | :---: | :---: | :---: | --- |
| EnkiApiScope (any signed-in) | Y | Y | Y | Y | Y | — |
| CanAccessTenant | Y¹ | Y¹ | Y¹ | Y² | Y | — |
| CanWriteTenantContent | — | Y¹ | Y¹ | — | Y | — |
| CanDeleteTenantContent | — | Y¹ | Y¹ | — | Y | — |
| CanManageTenantMembers | — | — | Y¹ | — | Y | — |
| CanWriteMasterContent | — | Y | Y | — | Y | — |
| CanDeleteMasterContent | — | Y | Y | — | Y | — |
| CanManageMasterTools | — | — | Y | — | Y | — |
| CanProvisionTenants | — | — | Y | — | Y | — |
| CanManageTenantLifecycle | — | — | Y | — | Y | — |
| CanReadMasterRoster | — | — | Y | — | Y | — |
| CanManageLicensing | — | Y³ | Y | — | Y | `licensing` |
| EnkiAdminOnly | — | — | — | — | Y | — |
| (Identity) EnkiAdmin | — | — | — | — | Y | — |
| (Identity) EnkiAdminOrOffice | — | Y⁴ | Y | — | Y | — |

¹ Only when the caller is a `TenantUser` member of the tenant in URL scope.
² Only when the route's `{tenantCode}` resolves to the user's bound `tenant_id`.
³ Only when the user holds the `licensing` capability claim.
⁴ Office can only act on Tenant-type *target* users; per-action body check (`RequireSufficientAuthorityFor`) blocks Office from mutating Team-type targets.

The cells in the per-entity tables below use the same shorthand (Y / — / Y¹ etc.).

\newpage

# 5. Master-level entities

These endpoints don't carry a `{tenantCode}` route parameter. Most require Office+ or Supervisor+; Tenant-type users are denied every master endpoint.

## 5.1 Tenant

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List tenants (filtered to membership) | GET | /tenants | EnkiApiScope | Y¹ | Y¹ | Y¹ | Y² | Y |
| Get tenant detail | GET | /tenants/{code} | CanAccessTenant | Y¹ | Y¹ | Y¹ | Y² | Y |
| Provision (create) tenant | POST | /tenants | CanProvisionTenants | — | — | Y | — | Y |
| Update tenant metadata | PUT | /tenants/{code} | CanWriteMasterContent | — | Y | Y | — | Y |
| Deactivate tenant | POST | /tenants/{code}/deactivate | CanManageTenantLifecycle | — | — | Y | — | Y |
| Reactivate tenant | POST | /tenants/{code}/reactivate | CanManageTenantLifecycle | — | — | Y | — | Y |

¹ List filters to TenantUser memberships; detail requires CanAccessTenant.
² Tenant-bound users see only their bound tenant.

## 5.2 TenantMember

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List tenant members | GET | /tenants/{code}/members | CanAccessTenant | Y¹ | Y¹ | Y¹ | Y² | Y |
| Add member | POST | /tenants/{code}/members | CanManageTenantMembers | — | — | Y¹ | — | Y |
| Remove member | DELETE | /tenants/{code}/members/{userId} | CanManageTenantMembers | — | — | Y¹ | — | Y |

## 5.3 Tool (master fleet)

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List tools | GET | /tools | EnkiApiScope | Y | Y | Y | Y | Y |
| Get tool detail | GET | /tools/{serial} | EnkiApiScope | Y | Y | Y | Y | Y |
| List calibrations for tool | GET | /tools/{serial}/calibrations | EnkiApiScope | Y | Y | Y | Y | Y |
| Create tool | POST | /tools | CanManageMasterTools | — | — | Y | — | Y |
| Update tool | PUT | /tools/{serial} | CanManageMasterTools | — | — | Y | — | Y |
| Retire / unretire tool | POST | /tools/{serial}/retire | CanManageMasterTools | — | — | Y | — | Y |
| Delete tool | DELETE | /tools/{serial} | CanManageMasterTools | — | — | Y | — | Y |

## 5.4 Calibration (master)

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| Get calibration | GET | /calibrations/{id} | EnkiApiScope | Y | Y | Y | Y | Y |
| Get processing defaults | GET | /calibrations/processing-defaults | EnkiApiScope | Y | Y | Y | Y | Y |
| Create calibration | POST | /calibrations | CanWriteMasterContent | — | Y | Y | — | Y |
| Update calibration | PUT | /calibrations/{id} | CanWriteMasterContent | — | Y | Y | — | Y |
| Delete calibration | DELETE | /calibrations/{id} | CanDeleteMasterContent | — | Y | Y | — | Y |

## 5.5 License

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List licenses | GET | /licenses | CanManageLicensing | — | Y³ | Y | — | Y |
| Get license detail | GET | /licenses/{id} | CanManageLicensing | — | Y³ | Y | — | Y |
| Generate license | POST | /licenses | CanManageLicensing | — | Y³ | Y | — | Y |
| Download license file | GET | /licenses/{id}/file | CanManageLicensing | — | Y³ | Y | — | Y |
| Download license key | GET | /licenses/{id}/key | CanManageLicensing | — | Y³ | Y | — | Y |
| Revoke license | POST | /licenses/{id}/revoke | CanManageLicensing | — | Y³ | Y | — | Y |

³ Office cell is Y only when the user holds the `licensing` capability claim.

## 5.6 Master users roster

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List master users (picker) | GET | /admin/master-users | CanReadMasterRoster | — | — | Y | — | Y |
| Sync master user from Identity | POST | /admin/master-users/sync | CanWriteMasterContent | — | Y | Y | — | Y |

## 5.7 System settings

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List all settings | GET | /admin/settings | EnkiAdminOnly | — | — | — | — | Y |
| Update setting by key | PUT | /admin/settings/{key} | EnkiAdminOnly | — | — | — | — | Y |
| Get Job region suggestions | GET | /jobs/region-suggestions | EnkiApiScope | Y | Y | Y | Y | Y |

## 5.8 Master audit feed

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List master audit | GET | /admin/audit/master | EnkiAdminOnly | — | — | — | — | Y |
| Export audit CSV | GET | /admin/audit/master/csv | EnkiAdminOnly | — | — | — | — | Y |
| Get entity history | GET | /admin/audit/master/{entityType}/{entityId} | EnkiAdminOnly | — | — | — | — | Y |

\newpage

# 6. Tenant-scoped entities

All endpoints below carry a `/{tenantCode}` route segment. For Team users, every cell marked Y also requires being a `TenantUser` member of the tenant in URL scope. For Tenant-bound users, every cell marked Y also requires the route's `{tenantCode}` to resolve to their bound `tenant_id`.

## 6.1 Job

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List jobs | GET | /tenants/{c}/jobs | CanAccessTenant | Y | Y | Y | Y | Y |
| Get job detail | GET | /tenants/{c}/jobs/{jobId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Create job | POST | /tenants/{c}/jobs | CanWriteTenantContent | — | Y | Y | — | Y |
| Update job | PUT | /tenants/{c}/jobs/{jobId} | CanWriteTenantContent | — | Y | Y | — | Y |
| Activate job | POST | /tenants/{c}/jobs/{jobId}/activate | CanWriteTenantContent | — | Y | Y | — | Y |
| Archive job | POST | /tenants/{c}/jobs/{jobId}/archive | CanDeleteTenantContent | — | Y | Y | — | Y |
| Restore job | POST | /tenants/{c}/jobs/{jobId}/restore | CanWriteTenantContent | — | Y | Y | — | Y |

## 6.2 Well

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List wells | GET | …/jobs/{j}/wells | CanAccessTenant | Y | Y | Y | Y | Y |
| Get well detail | GET | …/wells/{wellId} | CanAccessTenant | Y | Y | Y | Y | Y |
| List trajectories | GET | …/wells/trajectories | CanAccessTenant | Y | Y | Y | Y | Y |
| Anti-collision scan | GET | …/wells/{wellId}/anti-collision | CanAccessTenant | Y | Y | Y | Y | Y |
| List archived wells | GET | …/wells/archived | CanAccessTenant | Y | Y | Y | Y | Y |
| Create well | POST | …/wells | CanWriteTenantContent | — | Y | Y | — | Y |
| Update well | PUT | …/wells/{wellId} | CanWriteTenantContent | — | Y | Y | — | Y |
| Restore well | POST | …/wells/{wellId}/restore | CanWriteTenantContent | — | Y | Y | — | Y |
| Delete (soft-archive) well | DELETE | …/wells/{wellId} | CanDeleteTenantContent | — | Y | Y | — | Y |

## 6.3 Tie-on

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List tie-ons | GET | …/wells/{w}/tieons | CanAccessTenant | Y | Y | Y | Y | Y |
| Get tie-on detail | GET | …/tieons/{tieOnId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Create tie-on | POST | …/tieons | CanWriteTenantContent | — | Y | Y | — | Y |
| Update tie-on | PUT | …/tieons/{tieOnId} | CanWriteTenantContent | — | Y | Y | — | Y |
| Reset tie-on (soft delete) | DELETE | …/tieons/{tieOnId} | CanDeleteTenantContent | — | Y | Y | — | Y |

## 6.4 Survey

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List surveys | GET | …/wells/{w}/surveys | CanAccessTenant | Y | Y | Y | Y | Y |
| Get survey | GET | …/surveys/{surveyId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Create survey | POST | …/surveys | CanWriteTenantContent | — | Y | Y | — | Y |
| Bulk create surveys | POST | …/surveys/bulk | CanWriteTenantContent | — | Y | Y | — | Y |
| Update survey | PUT | …/surveys/{surveyId} | CanWriteTenantContent | — | Y | Y | — | Y |
| Recalculate trajectory | POST | …/surveys/calculate | CanWriteTenantContent | — | Y | Y | — | Y |
| Import surveys (CSV/LAS) | POST | …/surveys/import | CanWriteTenantContent | — | Y | Y | — | Y |
| Delete survey | DELETE | …/surveys/{surveyId} | CanDeleteTenantContent | — | Y | Y | — | Y |

## 6.5 Tubular

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List tubulars | GET | …/wells/{w}/tubulars | CanAccessTenant | Y | Y | Y | Y | Y |
| Get tubular | GET | …/tubulars/{tubularId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Create tubular | POST | …/tubulars | CanWriteTenantContent | — | Y | Y | — | Y |
| Update tubular | PUT | …/tubulars/{tubularId} | CanWriteTenantContent | — | Y | Y | — | Y |
| Delete tubular | DELETE | …/tubulars/{tubularId} | CanDeleteTenantContent | — | Y | Y | — | Y |

## 6.6 Formation

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List formations | GET | …/wells/{w}/formations | CanAccessTenant | Y | Y | Y | Y | Y |
| Get formation | GET | …/formations/{formationId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Create formation | POST | …/formations | CanWriteTenantContent | — | Y | Y | — | Y |
| Update formation | PUT | …/formations/{formationId} | CanWriteTenantContent | — | Y | Y | — | Y |
| Delete formation | DELETE | …/formations/{formationId} | CanDeleteTenantContent | — | Y | Y | — | Y |

## 6.7 Common measure

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List common measures | GET | …/wells/{w}/common-measures | CanAccessTenant | Y | Y | Y | Y | Y |
| Get common measure | GET | …/common-measures/{measureId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Create common measure | POST | …/common-measures | CanWriteTenantContent | — | Y | Y | — | Y |
| Update common measure | PUT | …/common-measures/{measureId} | CanWriteTenantContent | — | Y | Y | — | Y |
| Delete common measure | DELETE | …/common-measures/{measureId} | CanDeleteTenantContent | — | Y | Y | — | Y |

## 6.8 Magnetics (per-well reference)

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| Get well's magnetic reference | GET | …/wells/{w}/magnetics | CanAccessTenant | Y | Y | Y | Y | Y |
| Set / upsert reference | PUT | …/wells/{w}/magnetics | CanWriteTenantContent | — | Y | Y | — | Y |
| Clear reference | DELETE | …/wells/{w}/magnetics | CanDeleteTenantContent | — | Y | Y | — | Y |

## 6.9 Run

`RunsController` uses class-level `CanAccessTenant` only — every action inherits it, including writes. This is deliberate: Runs are the rig-side rotational write surface for Field operators and Tenant-bound users.

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List runs | GET | …/jobs/{j}/runs | CanAccessTenant | Y | Y | Y | Y | Y |
| Get run detail | GET | …/runs/{runId} | CanAccessTenant | Y | Y | Y | Y | Y |
| List archived runs | GET | …/runs/archived | CanAccessTenant | Y | Y | Y | Y | Y |
| List run calibrations | GET | …/runs/{runId}/calibrations | CanAccessTenant | Y | Y | Y | Y | Y |
| Create run | POST | …/runs | CanAccessTenant | Y | Y | Y | Y | Y |
| Update run | PUT | …/runs/{runId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Start run | POST | …/runs/{runId}/start | CanAccessTenant | Y | Y | Y | Y | Y |
| Suspend run | POST | …/runs/{runId}/suspend | CanAccessTenant | Y | Y | Y | Y | Y |
| Complete run | POST | …/runs/{runId}/complete | CanAccessTenant | Y | Y | Y | Y | Y |
| Cancel run | POST | …/runs/{runId}/cancel | CanAccessTenant | Y | Y | Y | Y | Y |
| Restore archived run | POST | …/runs/{runId}/restore | CanAccessTenant | Y | Y | Y | Y | Y |
| Delete (soft-archive) run | DELETE | …/runs/{runId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Upload passive binary | POST | …/runs/{runId}/passive/binary | CanAccessTenant | Y | Y | Y | Y | Y |
| Get passive binary | GET | …/runs/{runId}/passive/binary | CanAccessTenant | Y | Y | Y | Y | Y |
| Delete passive binary | DELETE | …/runs/{runId}/passive/binary | CanAccessTenant | Y | Y | Y | Y | Y |
| Update passive config | PUT | …/runs/{runId}/passive/config | CanAccessTenant | Y | Y | Y | Y | Y |

## 6.10 Log

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List logs | GET | …/runs/{r}/logs | CanAccessTenant | Y | Y | Y | Y | Y |
| Get log detail | GET | …/logs/{logId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Create log | POST | …/runs/{r}/logs | CanWriteTenantContent | — | Y | Y | — | Y |
| Update log config | PUT | …/logs/{logId} | CanWriteTenantContent | — | Y | Y | — | Y |
| Upload log binary | POST | …/logs/{logId}/binary | CanWriteTenantContent | — | Y | Y | — | Y |
| Delete log | DELETE | …/logs/{logId} | CanDeleteTenantContent | — | Y | Y | — | Y |

## 6.11 Shot

`ShotsController` uses class-level `CanAccessTenant` only — every action inherits it. Shots are the rig-side measurement write path for Field operators and Tenant-bound users. Binary uploads (primary tool, gyro tool) are the operational hot path on every survey station.

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List shots | GET | …/runs/{r}/shots | CanAccessTenant | Y | Y | Y | Y | Y |
| Get shot detail | GET | …/shots/{shotId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Create shot | POST | …/runs/{r}/shots | CanAccessTenant | Y | Y | Y | Y | Y |
| Update shot | PUT | …/shots/{shotId} | CanAccessTenant | Y | Y | Y | Y | Y |
| Update shot config | PUT | …/shots/{shotId}/config | CanAccessTenant | Y | Y | Y | Y | Y |
| Update shot result | PUT | …/shots/{shotId}/result | CanAccessTenant | Y | Y | Y | Y | Y |
| Update gyro config | PUT | …/shots/{shotId}/gyro-config | CanAccessTenant | Y | Y | Y | Y | Y |
| Upload primary binary | POST | …/shots/{shotId}/binary | CanAccessTenant | Y | Y | Y | Y | Y |
| Get primary binary | GET | …/shots/{shotId}/binary | CanAccessTenant | Y | Y | Y | Y | Y |
| Delete primary binary | DELETE | …/shots/{shotId}/binary | CanAccessTenant | Y | Y | Y | Y | Y |
| Upload gyro binary | POST | …/shots/{shotId}/gyro-binary | CanAccessTenant | Y | Y | Y | Y | Y |
| Get gyro binary | GET | …/shots/{shotId}/gyro-binary | CanAccessTenant | Y | Y | Y | Y | Y |
| Delete gyro binary | DELETE | …/shots/{shotId}/gyro-binary | CanAccessTenant | Y | Y | Y | Y | Y |
| Get shot comments | GET | …/shots/{shotId}/comments | CanAccessTenant | Y | Y | Y | Y | Y |
| Add comment | POST | …/shots/{shotId}/comments | CanAccessTenant | Y | Y | Y | Y | Y |
| Delete shot | DELETE | …/shots/{shotId} | CanAccessTenant | Y | Y | Y | Y | Y |

## 6.12 Calibration (tenant snapshot)

Tenant-side Calibrations are immutable snapshots created automatically when a `ToolId` is assigned to a Run. There are no direct mutation endpoints.

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List calibrations for run | GET | …/runs/{r}/calibrations | CanAccessTenant | Y | Y | Y | Y | Y |
| Get calibration snapshot | GET | …/calibrations/{calId} | CanAccessTenant | Y | Y | Y | Y | Y |

## 6.13 Tenant audit

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List tenant audit (paginated) | GET | /tenants/{c}/audit | CanAccessTenant | Y | Y | Y | Y | Y |
| Export audit CSV | GET | /tenants/{c}/audit/csv | CanAccessTenant | Y | Y | Y | Y | Y |
| Get entity history | GET | /tenants/{c}/audit/{entityType}/{entityId} | CanAccessTenant | Y | Y | Y | Y | Y |

\newpage

# 7. Identity-host endpoints

The Identity host (port 5196) owns user management and self-service preferences. Its policies are independent from the WebApi's; see [`Identity Program.cs`](../src/SDI.Enki.Identity/Program.cs) for the bindings.

## 7.1 User administration (`/admin/users`)

The `EnkiAdminOrOffice` policy passes for Office+ AND admin, but actions whose target is a Team-type user further require admin (per-action `RequireSufficientAuthorityFor` check inside the controller body).

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List users (paginated) | GET | /admin/users | EnkiAdmin | — | — | — | — | Y |
| Get user detail | GET | /admin/users/{id} | EnkiAdmin | — | — | — | — | Y |
| Create user (Tenant target) | POST | /admin/users | EnkiAdminOrOffice | — | Y | Y | — | Y |
| Create user (Team target) | POST | /admin/users | EnkiAdminOrOffice⁴ | — | — | — | — | Y |
| Update user (Tenant target) | PUT | /admin/users/{id} | EnkiAdminOrOffice | — | Y | Y | — | Y |
| Update user (Team target) | PUT | /admin/users/{id} | EnkiAdminOrOffice⁴ | — | — | — | — | Y |
| Lock account (Tenant target) | POST | /admin/users/{id}/lock | EnkiAdminOrOffice | — | Y | Y | — | Y |
| Lock account (Team target) | POST | /admin/users/{id}/lock | EnkiAdminOrOffice⁴ | — | — | — | — | Y |
| Unlock account (Tenant target) | POST | /admin/users/{id}/unlock | EnkiAdminOrOffice | — | Y | Y | — | Y |
| Unlock account (Team target) | POST | /admin/users/{id}/unlock | EnkiAdminOrOffice⁴ | — | — | — | — | Y |
| Force password reset (Tenant target) | POST | /admin/users/{id}/reset-password | EnkiAdminOrOffice | — | Y | Y | — | Y |
| Force password reset (Team target) | POST | /admin/users/{id}/reset-password | EnkiAdminOrOffice⁴ | — | — | — | — | Y |
| Override session lifetime (Tenant target) | POST | /admin/users/{id}/session-lifetime | EnkiAdminOrOffice | — | Y | Y | — | Y |
| Override session lifetime (Team target) | POST | /admin/users/{id}/session-lifetime | EnkiAdminOrOffice⁴ | — | — | — | — | Y |
| Flip `IsEnkiAdmin` | POST | /admin/users/{id}/admin | EnkiAdmin | — | — | — | — | Y |
| Grant capability claim | POST | /admin/users/{id}/capabilities/{cap} | EnkiAdmin | — | — | — | — | Y |
| Revoke capability claim | DELETE | /admin/users/{id}/capabilities/{cap} | EnkiAdmin | — | — | — | — | Y |

⁴ Per-target tightening: Office reaches the action via the policy gate, but the action body's `RequireSufficientAuthorityFor` check denies any mutation against a Team-type target. Only admin can mutate Team-type users.

## 7.2 Identity audit feeds

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List identity audit (admin actions) | GET | /admin/audit/identity | EnkiAdmin | — | — | — | — | Y |
| List auth events (sign-ins, lockouts, token issuance) | GET | /admin/audit/auth-events | EnkiAdmin | — | — | — | — | Y |
| Auth events CSV | GET | /admin/audit/auth-events/csv | EnkiAdmin | — | — | — | — | Y |

## 7.3 Self-service identity (`/me`)

Any signed-in user — no role gate beyond authentication.

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| Get my preferences | GET | /me/preferences | (any signed-in) | Y | Y | Y | Y | Y |
| Update my preferences | PUT | /me/preferences | (any signed-in) | Y | Y | Y | Y | Y |
| Change my password | POST | /me/change-password | (any signed-in) | Y | Y | Y | Y | Y |

## 7.4 Self-service WebApi (`/me`)

| Action | Verb | Route | Policy | Field | Office | Supervisor | Tenant-bound | enki-admin |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| List my tenant memberships | GET | /me/memberships | EnkiApiScope | Y | Y | Y | Y | Y |

\newpage

# 8. Cross-cutting rules

- **Tenant deactivation = hard 404.** When a tenant's `Status != Active`, the `TenantRoutingMiddleware` returns 404 (RFC 7807) on every tenant-scoped route, regardless of the caller's role. **enki-admin does NOT bypass this** — admins must reactivate via `POST /tenants/{code}/reactivate` (master-scoped, so unaffected) before tenant data is reachable again.
- **Membership cache.** The `(IdentityId, TenantCode)` membership decision is cached for 60 seconds. Adding or removing a member busts the entry via `MembershipCacheKey(...)` so changes take effect immediately for the affected user.
- **Tenant-id-to-code cache.** Tenant-bound users' bind-check uses a 60-second cache for the GUID → Code lookup. Lifecycle changes (deactivate/archive) invalidate it.
- **Soft-delete.** Wells, Runs, Jobs, and (cascade) Surveys/Logs/Shots use an `ArchivedAt` timestamp + global query filter rather than physical deletion. The `archived` listing endpoints bypass the filter for restore flows.
- **Denial auditing.** Every policy denial flows through `IAuthzDenialAuditor` and lands in `MasterAuditLog` with a structured reason (`NotAMember`, `InsufficientSubtypeOrCapability`, `TenantUserBoundToDifferentTenant`, etc.). Visible in the master audit feed (`EnkiAdminOnly`).
- **Defence-in-depth.** UI gating in Blazor mirrors policy decisions but does not enforce them — every API response goes through the same `TeamAuthHandler` check on the WebApi side. Hidden buttons are usability; the server's 403 is the security.

# 9. References

- Source: [`src/SDI.Enki.Shared/Authorization/EnkiPolicies.cs`](../src/SDI.Enki.Shared/Authorization/EnkiPolicies.cs) (policy names + intent)
- Decision tree: [`src/SDI.Enki.WebApi/Authorization/TeamAuthRequirement.cs`](../src/SDI.Enki.WebApi/Authorization/TeamAuthRequirement.cs)
- Claim materialisation: [`src/SDI.Enki.Identity/Data/EnkiUserClaimsPrincipalFactory.cs`](../src/SDI.Enki.Identity/Data/EnkiUserClaimsPrincipalFactory.cs)
- Routing 404: [`src/SDI.Enki.WebApi/Multitenancy/TenantRoutingMiddleware.cs`](../src/SDI.Enki.WebApi/Multitenancy/TenantRoutingMiddleware.cs)
- Identity host policies: [`src/SDI.Enki.Identity/Program.cs`](../src/SDI.Enki.Identity/Program.cs)

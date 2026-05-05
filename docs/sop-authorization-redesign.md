---
title: "Enki — Authorization Redesign"
subtitle: "Standard Operating Procedure"
author: "SDI · KingOfTheGeeks"
date: "2026-05-02"
---

# Enki — Authorization Redesign

**Standard Operating Procedure**

| Field | Value |
| --- | --- |
| Document number | SDI-ENG-SOP-002 |
| Version | 1.1 (draft) |
| Effective date | 2026-05-01 |
| Document owner | Mike King — KingOfTheGeeks |
| Issuing organization | SDI Engineering |
| Status | Draft — pending client review |
| Related repo | <https://github.com/KingOfTheGeeks/Enki> |
| Related commit | `01206c2` (`feat(authz): subtype + capability authorization with parametric policy`) |
| Reviewed by | _________________ |
| Approved by | _________________ |

---

## A. Purpose

This Standard Operating Procedure documents the authorization model used by the Enki platform — who can perform which actions, how those decisions are made by the system, and how user permissions are administered.

It is intended for client review of the role and permission system being introduced in this release. It supersedes any informal description of "user roles" in earlier releases.

## B. Scope

**In scope:** every user-facing capability surfaced by the four runnable Enki hosts (Identity, WebApi, BlazorServer, Migrator), the user classification system (Team subtypes, Tenant users, capability claims), the thirteen named authorization policies, and the per-action authorization rules applied across the WebApi.

**Out of scope:** computational behavior of Marduk (handled separately), licensing-asset packaging via Nabu, the field-side Esagila desktop tool. These have or will have their own SOPs.

**Audience:** SDI engineering, client administrators, project sponsors.

## C. Responsibilities

| Role | Responsibility |
| --- | --- |
| **System Administrator (`enki-admin`)** | Provision and manage user accounts; grant the System Administrator role and capability claims; perform any operation in the system. Designated explicitly during user provisioning. |
| **Supervisor** (Team subtype) | Provision new tenants; manage tenant lifecycle; manage tenant memberships; manage the master Tools registry; issue and revoke licenses. |
| **Office** (Team subtype) | Day-to-day operational management: jobs, wells, surveys, calibrations, tenant settings, creating Tenant-type accounts. |
| **Field** (Team subtype) | Read-only access to tenant data plus operational write on Runs, Shots, and bin uploads. |
| **Tenant user** | External customer account hard-bound to one tenant; today functionally equivalent to Field within their bound tenant. Dedicated controllers are planned for a later release; until then the same controllers are used and access is enforced by the bound-tenant check. |

---

## D. User types

Every user in Enki is one of two types. The type is chosen at account creation and is **immutable** afterwards. To change a user from one type to the other, a new account must be created.

### D.1 Team users (SDI internal)

SDI employees and contractors. Subdivided into three sub-classifications (TeamSubtype): Field, Office, Supervisor. See section E.

Team users:

- Can be added to one or more tenants via tenant membership grants.
- Can hold capability claims (section F) that elevate them for a specific class of operation.
- Can be promoted to the System Administrator role.

### D.2 Tenant users (external customer)

External customer accounts. Hard-bound to **exactly one** tenant — they cannot access any data outside that tenant.

Tenant users:

- Cannot be promoted to the System Administrator role.
- Cannot hold capability claims.
- Cannot be added to additional tenants.
- During the interim period (before the dedicated Tenant portal lands), use the same controllers as Team users with an effective Field-equivalent capability set inside their bound tenant.

---

## E. Capability matrix

The table below summarises what each persona can do across the WebApi surface. **System Administrator (`enki-admin`) bypasses every subtype and membership gate.** A separate column shows the effect of holding the **Licensing** capability claim (`+L`). The "T" column shows what a Tenant user can do during the interim period, scoped to their bound tenant.

| Capability | Field | Office | Supervisor | Admin | +L | Tenant (interim) |
| --- | :---: | :---: | :---: | :---: | :---: | :---: |
| Read tenant data (Jobs, Wells, Surveys, …) | ✓ | ✓ | ✓ | ✓ |  | ✓ (bound tenant only) |
| Read master Tools, master Calibrations | ✓ | ✓ | ✓ | ✓ |  | ✓ |
| Read per-tenant audit feed | ✓ | ✓ | ✓ | ✓ |  | ✓ (bound tenant only) |
| Add / edit / use Runs, Shots, and Logs; upload bin files (incl. log binaries + result files) | ✓ | ✓ | ✓ | ✓ |  | ✓ (bound tenant only) |
| Add / edit / delete Jobs, Wells, TieOns, Surveys, Tubulars, Formations, Common Measures, Magnetics |  | ✓ | ✓ | ✓ |  |  |
| Edit existing tenant settings (display name, notes, contact email) |  | ✓ | ✓ | ✓ |  |  |
| Add / edit / delete master Calibrations; upload calibration binaries |  | ✓ | ✓ | ✓ |  |  |
| Sync a master User row mirroring an Identity row (`POST /admin/master-users/sync`) |  | ✓ | ✓ | ✓ |  |  |
| Provision new Tenant-type users; edit / lock / unlock / reset password / change session lifetime on Tenant-type users |  | ✓ | ✓ | ✓ |  |  |
| Add / edit / delete master Tools |  |  | ✓ | ✓ |  |  |
| Provision new tenants (creates SQL Server DB pair) |  |  | ✓ | ✓ |  |  |
| Deactivate / Reactivate a tenant |  |  | ✓ | ✓ |  |  |
| Add / remove tenant members |  |  | ✓ | ✓ |  |  |
| Read the master User picker (`GET /admin/master-users` — fuels the tenant-member dialog) |  |  | ✓ | ✓ |  |  |
| Generate / Revoke licenses |  |  | ✓ | ✓ | ✓ |  |
| Provision new Team-type users; perform admin operations on Team-type users (lock, reset password, change session lifetime, change classification, grant / revoke admin role, grant / revoke capabilities) |  |  |  | ✓ |  |  |
| Read the cross-tenant master audit feed, identity audit, auth events |  |  |  | ✓ |  |  |
| Edit system settings |  |  |  | ✓ |  |  |

### E.1 Note on the universal read floor (`EnkiApiScope`)

A small number of read endpoints (master Tools list/detail, master Calibrations detail, processing-defaults read, the per-user `/me/memberships` probe) sit on a default policy that requires only "any signed-in user with an `enki`-scoped access token". These endpoints are intentionally open to every signed-in user including Tenant users — they are reference-data reads with no per-tenant payload.

### E.2 Field

Operational role for field engineers running tools downhole. Field users:

- See every tenant they are a member of.
- Can read Jobs, Wells, Surveys, etc., but cannot modify them.
- Can create, edit, and delete Runs, Shots, and Logs — these represent operational work in progress.
- Can upload bin files for processing (shot binaries, log binaries, log result files).
- Can read per-tenant audit feeds.

### E.3 Office

Day-to-day operational management. Office users can do everything Field can, plus:

- Create, edit, and delete tenant content (Jobs, Wells, TieOns, Surveys, Tubulars, Formations, Common Measures, Magnetics).
- Edit existing tenant settings (display name, notes, contact email).
- Create, edit, and delete master Calibrations.
- Provision new Tenant-type users; perform full admin operations on existing Tenant-type users.

Office users **cannot** provision new tenants, manage tenant lifecycle, manage tenant members, touch master Tools, or generate licenses (without the Licensing capability).

### E.4 Supervisor

Senior operational role. Supervisor users can do everything Office can, plus:

- Manage master Tools (the fleet-wide tool registry).
- Provision new tenants (creates the SQL Server database pair).
- Deactivate and reactivate tenants.
- Add and remove tenant members.
- Read the master User picker (used by the "Add member" dialog).
- Generate and revoke licenses.

Supervisors **cannot** perform admin operations on Team-type users (those are System Administrator only), and cannot read the cross-tenant master audit feed.

---

## F. Capability claims

Capability claims are atomic permissions granted to a specific user, independently of their TeamSubtype. They allow a trusted Office user to perform a single class of operation that would otherwise require Supervisor.

| Capability | What it grants | Granted via |
| --- | --- | --- |
| **Licensing** | Generate and revoke licenses, regardless of TeamSubtype. Combined OR with the Supervisor subtype gate. | An Administrator grants it via the user detail page in the admin area. |

Capability claims are a **Team-side construct only.** Tenant users cannot hold capabilities.

### F.1 Effect on existing sessions

Granting or revoking a capability rotates the user's security stamp. The user keeps their currently-issued access token until it expires (~15 minutes by default). On the next refresh-token exchange the system observes the rotated stamp and forces a fresh sign-in, after which the new capability set is reflected in their token.

For an immediate cut-off (revoke takes effect within seconds rather than minutes), an Administrator can additionally lock the account, perform the revoke, then unlock — locking invalidates the access token immediately.

### F.2 Future capabilities

The capability surface is extensible by adding constants to `EnkiCapabilities.All` in shared code. New capabilities added there auto-render as checkboxes on the user detail page; the API gate is created by referencing the capability name in a `TeamAuthRequirement`. No bespoke handler code is required per capability.

---

## G. Administrative privileges (System Administrator only)

The following operations are reserved for System Administrators (`IsEnkiAdmin = true`) and cannot be delegated to Supervisors or below:

- Provisioning new Team-type users.
- Editing profile, classification, or session lifetime of any Team-type user.
- Locking, unlocking, or resetting passwords on Team-type users.
- Granting or revoking the System Administrator role.
- Granting or revoking capability claims.
- Editing system settings.
- Reading the cross-tenant master audit feed (`/admin/audit/master`), identity audit (`/admin/audit/identity`), and auth events (`/admin/audit/auth-events`).

Per-tenant audit (`/tenants/{code}/audit`) is open to any tenant member and is not in this admin-only list.

---

## H. Per-tenant access (membership)

Subtype determines **which actions** a user can perform; tenant membership determines **which tenants** those actions can be performed in.

- A Team user becomes a member of a tenant when a Supervisor or Administrator adds them on the tenant's Members page.
- A user without any tenant memberships can sign in but sees no tenant data.
- The System Administrator role bypasses tenant membership — administrators can access every tenant.
- Tenant-type users do not appear in the tenant membership table; they are bound directly to one tenant via their account, and the bound-tenant check on every per-tenant request enforces the boundary.

---

## I. The thirteen named policies

Authorization is structured around thirteen named policies. Each `[Authorize(Policy = …)]` attribute on the WebApi references one of these by name. Constants live in `SDI.Enki.Shared.Authorization.EnkiPolicies` and are referenced identically by both the WebApi and the BlazorServer hosts so a renamed policy fails compilation in both.

| Policy | Audience | Notes |
| --- | --- | --- |
| `EnkiApiScope` | Any signed-in caller with the `enki` scope | Default fallback; covers reference-data reads. |
| `CanAccessTenant` | Tenant member or admin (Tenant-type users pass for their bound tenant) | Tenant-scoped read gate, Runs/Shots writes. |
| `CanWriteTenantContent` | Office+ tenant member or admin | Tenant-scoped write gate. |
| `CanDeleteTenantContent` | Office+ tenant member or admin | Same gate as `CanWriteTenantContent` today; kept as a separate name so a future "delete needs Supervisor" tightening is a one-line policy change with no controller churn. |
| `CanManageTenantMembers` | Supervisor+ tenant member or admin | Tenant member add/remove. |
| `CanWriteMasterContent` | Office+ or admin | Calibrations, tenant settings, Tenant-user creation, master-User sync. |
| `CanDeleteMasterContent` | Office+ or admin | Calibration deletes. Same gate as `CanWriteMasterContent`; kept as a separate name for the same forward-tightening reason. |
| `CanManageMasterTools` | Supervisor+ or admin | Master Tools CRUD. |
| `CanProvisionTenants` | Supervisor+ or admin | Tenant provisioning (creates SQL DBs). |
| `CanManageTenantLifecycle` | Supervisor+ or admin | Deactivate, reactivate. |
| `CanReadMasterRoster` | Supervisor+ or admin | `GET /admin/master-users` — picker for the Add-member dialog. |
| `CanManageLicensing` | Supervisor+ OR holder of `Licensing` capability OR admin | License generation and revocation. |
| `EnkiAdminOnly` | System Administrator (`enki-admin`) only | Cross-tenant administrative endpoints — system settings, master audit feed, identity audit, auth events. |

Twelve of the thirteen policies are constructed in the WebApi from a single parametric `TeamAuthRequirement` evaluated by a single handler with an 8-step decision tree (admin → Tenant-type binding → membership → subtype → capability); the thirteenth, `EnkiApiScope`, is the default scope-only policy used as a fallback. The BlazorServer host registers parallel claim-assertion policies under the same names so `[Authorize(Policy = EnkiPolicies.CanFoo)]` works on Blazor pages too.

---

## J. Behavior changes from prior releases

Each item below changes existing customer behavior and is documented for client awareness.

### J.1 Per-tenant role retired

The previous **Admin / Contributor / Viewer** per-tenant role on a tenant membership has been removed. The database column is dropped (folded into the consolidated `Initial` master-DB migration during the pre-customer schema squash) and the `SetRole` action is gone from the API. Member management is now keyed off the system-wide TeamSubtype hierarchy: Supervisor or Administrator only.

**Customer impact:** any user who relied on holding the per-tenant `Admin` role (without also being a system Supervisor) for member management will no longer have that capability. Confirm the affected users have been promoted to Supervisor where appropriate.

### J.2 New "Tenant" user type

External customer accounts are now provisioned as Tenant-type users, hard-bound to a single tenant. Earlier releases used Team-type accounts with a single tenant membership for this purpose; existing accounts are not migrated automatically — they continue to function as Team users.

### J.3 Office can now manage Tenant users

Previously, all user administration required System Administrator. Office-tier users can now create new Tenant-type users and perform full admin operations on existing Tenant-type users (edit profile, lock/unlock, reset password, change session lifetime). Team-type user administration remains System Administrator only.

### J.4 Tools and Calibrations write access

Previously, any signed-in user could create or edit master Tools and master Calibrations. After this change:

- Master **Calibrations** writes require Office or higher.
- Master **Tools** writes require Supervisor or higher.

Reads remain open to all signed-in users so field engineers can identify the tool they are operating.

### J.5 License operations broadened

License generation and revocation, previously System-Administrator-only, are now available to:

- Supervisors (by virtue of subtype), and
- Any user holding the **Licensing** capability claim (typically a trusted Office user designated by an Administrator).

### J.6 Per-tenant audit visible to tenant members

The per-tenant audit feed (`/tenants/{code}/audit`) is open to any tenant member, not only administrators. This is unchanged from prior behavior but is now explicitly stated to disambiguate it from the cross-tenant master audit (`/admin/audit/*`) which remains administrator-only.

### J.7 Logs writes broadened to Field + Tenant-bound

Previously, Log writes (create, update, upload binary, upload result file, delete) required Office or higher — Logs were grouped with the Office+ tenant-content set alongside Tubulars, Formations, etc. After this change, the entire `LogsController` surface sits on the same class-level `CanAccessTenant` gate as `RunsController` and `ShotsController`, so Field operators and Tenant-bound users can now write Logs on tenants they belong to.

**Why:** Logs are rig-side sensor capture (the stream the Field operator hands off during trip in/out of hole), not office configuration. The original Office+ scoping was inconsistent with the operational model that already applies to Runs and Shots.

**Customer impact:** none beyond a permissions widening — no existing user loses access. Field and Tenant-bound users gain the ability to create/edit/delete Logs and upload log binaries inside tenants they're already members of.

---

## K. Acceptance criteria

This SOP is approved when:

1. The capability matrix in section E and the policy list in section I are validated by SDI engineering against the deployed code.
2. The behavior changes in section J are reviewed and accepted.
3. A representative System Administrator confirms ability to provision a Tenant-type user, grant the Licensing capability to a trusted Office user, and revoke either independently.
4. A representative Supervisor confirms ability to perform tenant lifecycle operations (deactivate / reactivate), add and remove tenant members, and issue a license.
5. A representative Office user confirms inability to provision tenants and inability to manage master Tools, but ability to manage Tenant-type users and to create master Calibrations.
6. A representative Field user (or Tenant user) confirms ability to add Runs and Shots within an accessible tenant, and inability to add Jobs.

---

# Document control

## Revision history

| Version | Date | Author | Changes |
| --- | --- | --- | --- |
| 1.0 (draft) | 2026-05-01 | Mike King (KingOfTheGeeks) | Initial draft. Documented the authorization redesign across user types, subtypes, capability claims, administrative privileges, per-tenant membership, and migration impact. |
| 1.1 (draft) | 2026-05-02 | Mike King (KingOfTheGeeks) | Replacement aligned to commit `01206c2`. Added section I documenting the twelve named policies. Split the matrix to distinguish per-tenant audit (any member) from cross-tenant master audit (admin only). Added the universal read floor (`EnkiApiScope`) note. Soften "session invalidation" wording in section F to reflect the security-stamp + refresh-token mechanism. Added a `master-User sync` row and a master-User-picker row. Added a future-capabilities subsection (F.2). Added per-tenant audit acknowledgement (J.6). |

## Change-control protocol

Updates to this SOP follow the standard procedure-change rules:

1. Every code change that alters a permission gate (a new capability, a moved policy, a changed default) **requires** a corresponding update to the matrix in section E **and** the policy list in section I in the same pull request.
2. Adding or removing a TeamSubtype, UserType, or named policy bumps the SOP minor version (1.1 → 1.2). Renumbering the matrix or restructuring sections bumps the major version (1.x → 2.0).
3. Every SOP version is tagged in source control alongside the Enki release it covers.

## Storage and distribution

The authoritative source of this SOP is the markdown in the Enki repository (`docs/sop-authorization-redesign.md`). The compiled `.docx` artifact (`docs/sop-authorization-redesign.docx`) is regenerated from that source at release time and distributed to client administrators and project sponsors as needed.

Print copies are uncontrolled. The source-of-truth is the version in the repository tagged for the release under review.

## Approval

By signing below, the approver attests that this SOP correctly documents the authorization model in the corresponding Enki release.

|  | Name | Role | Signature | Date |
| --- | --- | --- | --- | --- |
| Reviewed by |  |  |  |  |
| Approved by |  |  |  |  |
| Client sign-off |  |  |  |  |

---
title: "Enki — Authorization Redesign"
subtitle: "Standard Operating Procedure"
author: "SDI · KingOfTheGeeks"
date: "2026-05-01"
---

# Enki — Authorization Redesign

**Standard Operating Procedure**

| Field | Value |
| --- | --- |
| Document number | SDI-ENG-SOP-002 |
| Version | 1.0 (draft) |
| Effective date | 2026-05-01 |
| Document owner | Mike King — KingOfTheGeeks |
| Issuing organization | SDI Engineering |
| Status | Draft — pending client review |
| Related repo | <https://github.com/KingOfTheGeeks/Enki> |
| Reviewed by | _________________ |
| Approved by | _________________ |

---

## A. Purpose

This Standard Operating Procedure documents the authorization model used by the Enki platform — who can perform which actions, how those decisions are made, and how user permissions are administered.

It is intended for client review of the role and permission system being introduced in this release. It supersedes any informal description of "user roles" in earlier releases.

## B. Scope

**In scope:** every user-facing capability surfaced by the four runnable Enki hosts (Identity, WebApi, BlazorServer, Migrator), the new user classification system (Team subtypes, Tenant users, capability claims), and the per-action authorization rules.

**Out of scope:** computational behavior of Marduk (handled separately), licensing-asset packaging via Nabu, the field-side Esagila desktop tool. These are documented in their own SOPs.

**Audience:** SDI engineering, client administrators, project sponsors.

## C. Responsibilities

| Role | Responsibility |
| --- | --- |
| **System Administrator (`enki-admin`)** | Provision and manage user accounts; grant special permissions; perform any operation in the system. One or more users designated up-front. |
| **Supervisor** | Provision new tenants; manage tenant lifecycle (deactivate / archive); manage tenant memberships; issue and revoke licenses. |
| **Office** | Day-to-day operational management: jobs, wells, surveys, calibrations, tenant settings, creating Tenant-type accounts. |
| **Field** | Read-only access plus operational write on Runs, Shots, and bin uploads. |
| **Tenant user** | External customer account hard-bound to one tenant; today equivalent to Field within their bound tenant. Dedicated controllers planned for a later release. |

---

## D. User types

Every user in Enki is one of two types. The type is chosen at account creation and is **immutable** afterwards. To change a user from one type to the other, a new account must be created.

### D.1 Team users (SDI internal)

SDI employees and contractors. Subdivided into three sub-classifications (TeamSubtype) listed in section E.

Team users:

- Can be added to one or more tenants via tenant membership grants.
- Can hold special permissions ("capability claims") that elevate them for specific operations.
- Can be promoted to System Administrator.

### D.2 Tenant users (external customer)

External customer accounts. Hard-bound to **exactly one** tenant — they cannot access any data outside that tenant.

Tenant users:

- Cannot be promoted to System Administrator.
- Cannot hold capability claims.
- Cannot be added to additional tenants.
- During the interim period (before the dedicated Tenant portal lands), use the same controllers as Team users with an effective Field-equivalent capability set inside their bound tenant.

---

## E. Team subtypes — capability matrix

The following table summarises what each Team subtype can do. **System Administrator (`enki-admin`) bypasses every subtype gate.** A separate column shows the effect of holding the **Licensing** capability claim (`+L`).

The column "T" shows what a Tenant user can do during the interim period, scoped to their bound tenant.

| Capability | Field | Office | Supervisor | Admin | +L | Tenant (interim) |
| --- | :---: | :---: | :---: | :---: | :---: | :---: |
| Read tenant data, master Tools, master Calibrations | ✓ | ✓ | ✓ | ✓ |  | ✓ (bound tenant only) |
| Add / edit / use Runs, Shots, bin uploads | ✓ | ✓ | ✓ | ✓ |  | ✓ (bound tenant only) |
| Add / edit / delete Jobs, Wells, TieOns, Surveys, Tubulars, Formations, Common Measures, Magnetics, Logs, Comments |  | ✓ | ✓ | ✓ |  |  |
| Edit existing tenant settings (display name, notes, contact email) |  | ✓ | ✓ | ✓ |  |  |
| Add / edit / delete master Calibrations |  | ✓ | ✓ | ✓ |  |  |
| Run computation pipelines |  | ✓ | ✓ | ✓ |  |  |
| Provision new Tenant-type users; edit / lock / unlock / reset password / change session lifetime on Tenant-type users |  | ✓ | ✓ | ✓ |  |  |
| Add / edit / delete master Tools |  |  | ✓ | ✓ |  |  |
| Provision new tenants |  |  | ✓ | ✓ |  |  |
| Deactivate / Reactivate / Archive a tenant |  |  | ✓ | ✓ |  |  |
| Add / remove tenant members |  |  | ✓ | ✓ |  |  |
| View the master user picker (roster) |  |  | ✓ | ✓ |  |  |
| Generate / Revoke licenses |  |  | ✓ | ✓ | ✓ |  |
| Provision new Team-type users; perform admin operations on Team-type users (lock, reset password, change session lifetime, change classification, grant / revoke admin role, grant / revoke capabilities) |  |  |  | ✓ |  |  |

### E.1 Field

Operational role for field engineers running tools downhole. Field users:

- See every tenant they are a member of.
- Can read Jobs, Wells, Surveys, etc., but cannot modify them.
- Can create, edit, and delete Runs and Shots — these represent operational work in progress.
- Can upload bin files for processing.

### E.2 Office

Day-to-day operational management. Office users can do everything Field can, plus:

- Create, edit, and delete tenant content (Jobs, Wells, TieOns, Surveys, Tubulars, Formations, Common Measures, Magnetics, Logs, Comments).
- Edit existing tenant settings (display name, notes, contact email).
- Create, edit, and delete master Calibrations.
- Run computation pipelines.
- Provision new Tenant-type users; perform full admin operations on existing Tenant-type users.

Office users **cannot** provision new tenants, manage tenant lifecycle, or touch master Tools / Licenses (without the Licensing capability).

### E.3 Supervisor

Senior operational role. Supervisor users can do everything Office can, plus:

- Manage master Tools (the fleet-wide tool registry).
- Provision new tenants (creates the SQL Server databases).
- Deactivate, reactivate, and archive tenants.
- Add and remove tenant members.
- View the master user picker.
- Generate and revoke licenses.

Supervisors **cannot** perform admin operations on Team-type users (those are System Administrator only).

---

## F. Special permissions (capability claims)

Capability claims are atomic permissions granted to a specific user, independently of their TeamSubtype. They allow a trusted Office user to perform a single class of operation that would otherwise require Supervisor.

| Capability | What it grants | Granted via |
| --- | --- | --- |
| **Licensing** | Generate and revoke licenses, regardless of TeamSubtype. Combined OR with the Supervisor subtype gate. | Admin grants via the user detail page in the admin area. |

Capability claims are a **Team-side construct only.** Tenant users cannot hold capabilities.

Granting or revoking a capability immediately invalidates the user's existing session — they will be required to sign in again on their next request to acquire the new permission set.

---

## G. Administrative privileges (System Administrator only)

The following operations are reserved for System Administrators (`IsEnkiAdmin = true`) and cannot be delegated to Supervisors or below:

- Provisioning new Team-type users.
- Editing profile or classification of any Team-type user.
- Locking, unlocking, or resetting passwords on Team-type users.
- Granting or revoking the System Administrator role itself.
- Granting or revoking capability claims.
- Editing system settings.
- Reading the system audit log.

---

## H. Per-tenant access (membership)

Subtype determines **which actions** a user can perform; tenant membership determines **which tenants** those actions can be performed in.

- A Team user becomes a member of a tenant when a Supervisor or Administrator adds them on the tenant's Members page.
- A user without any tenant memberships can sign in but sees no tenant data.
- The System Administrator role bypasses tenant membership — administrators can access every tenant.
- Tenant-type users do not appear in the tenant membership table; they are bound directly to one tenant via their account.

---

## I. Behavior changes from prior releases

The following changes affect existing customers. Each is documented for client awareness and review.

### I.1 Per-tenant role retired

The previous **Admin / Contributor / Viewer** per-tenant role on a tenant membership has been retired. Member management (adding and removing tenant members) is now keyed off the system-wide TeamSubtype hierarchy: Supervisor or Administrator only.

**Customer impact:** any user who relied on holding the per-tenant `Admin` role (without also being a system Supervisor) for member management will no longer have that capability. Confirm the affected users have been promoted to Supervisor where appropriate.

### I.2 New "Tenant" user type

External customer accounts are now provisioned as Tenant-type users, hard-bound to a single tenant. Earlier releases used Team-type accounts with a single tenant membership for this purpose; existing accounts are not migrated automatically — they continue to function as Team users.

### I.3 Office can now manage Tenant users

Previously, all user administration required System Administrator. Office-tier users can now create new Tenant-type users and perform full admin operations on existing Tenant-type users. Team-type user administration remains System Administrator only.

### I.4 Tools and Calibrations write access

Previously, any signed-in user could create or edit master Tools and master Calibrations (a known security gap). After this change:

- Master **Calibrations** writes require Office or higher.
- Master **Tools** writes require Supervisor or higher.

Reads remain open to all signed-in users so field engineers can identify the tool they are operating.

### I.5 License operations broadened

License generation and revocation, previously System-Administrator-only, are now available to:

- Supervisors (by virtue of subtype), and
- Any user holding the **Licensing** capability claim (typically a trusted Office user designated by an Administrator).

---

## J. Acceptance criteria

This SOP is approved when:

1. The capability matrix in section E is validated by a representative from each role.
2. The four behavior changes in section I are reviewed and accepted.
3. A representative System Administrator confirms ability to provision a Tenant-type user, grant the Licensing capability, and remove either independently.
4. A representative Supervisor confirms ability to perform tenant lifecycle operations and license issuance.
5. A representative Office user confirms inability to provision tenants but ability to manage Tenant-type users.

---

# Document control

## Revision history

| Version | Date | Author | Changes |
| --- | --- | --- | --- |
| 1.0 (draft) | 2026-05-01 | Mike King (KingOfTheGeeks) | Initial draft. Documents the authorization redesign across user types (Team / Tenant), Team subtypes (Field / Office / Supervisor), capability claims (Licensing), administrative privileges, per-tenant membership, and migration impact. |

## Change-control protocol

Updates to this SOP follow the standard procedure-change rules:

1. Every code change that alters a permission gate (a new capability, a moved policy, a changed default) **requires** a corresponding update to the matrix in section E in the same pull request.
2. Adding or removing a TeamSubtype, UserType, or capability claim bumps the SOP minor version (1.0 → 1.1). Renumbering the matrix or restructuring sections bumps the major version (1.x → 2.0).
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

---
title: "Enki — User Interface Gating"
subtitle: "Standard Operating Procedure"
author: "SDI · KingOfTheGeeks"
date: "2026-05-01"
---

# Enki — User Interface Gating

**Standard Operating Procedure**

| Field | Value |
| --- | --- |
| Document number | SDI-ENG-SOP-003 |
| Version | 1.0 (draft) |
| Effective date | 2026-05-01 |
| Document owner | Mike King — KingOfTheGeeks |
| Issuing organization | SDI Engineering |
| Status | Draft — pending client review |
| Related repo | <https://github.com/KingOfTheGeeks/Enki> |
| Related SOP | SDI-ENG-SOP-002 (Authorization Redesign) |
| Reviewed by | _________________ |
| Approved by | _________________ |

---

## A. Purpose

This Standard Operating Procedure documents how the Enki user interface adapts to each user's role and permissions — what menus, buttons, and form controls each persona will see, and how the application responds when a user navigates to a page they are not authorized to view.

It is intended for client review of the user-interface behavior changes being introduced alongside the authorization redesign (SDI-ENG-SOP-002). It is the authoritative reference for "what will my users see when they sign in?".

## B. Scope

**In scope:** every authorization-sensitive interface element in the Blazor Server web application — navigation menus, page-level access, action buttons, form fields, and the access-denied response when a user reaches a page their role does not permit.

**Out of scope:** field-side desktop tools (Esagila), the licensing-asset packaging tool (Nabu), any external integrations consuming Enki's REST API.

**Audience:** SDI engineering, client end users, client administrators, training and onboarding personnel.

## C. Responsibilities

| Role | Responsibility |
| --- | --- |
| **System Administrator** | Validates that the navigation menu and admin pages render as documented for the admin persona; signs off the GUI gating contract. |
| **Test operators (per persona)** | Sign in as each persona type (Field, Office, Supervisor, Tenant); confirm visible elements match this SOP. |
| **Client onboarding** | Use this SOP as the basis for end-user training material. |

---

## D. Interface gating principles

The Enki user interface follows three principles for permission-sensitive elements:

### D.1 Hide, don't disable

Controls a user can never use are **hidden from the interface entirely**. Disabled-with-tooltip is reserved for cases where the action could be granted to the user but is currently blocked by some state (for example, a "Delete" button disabled because the row has dependent records).

This keeps the interface uncluttered and avoids surfacing elements that imply the user could access them.

### D.2 The user interface is convenience; the API is security

Hidden buttons are a usability feature. The actual security enforcement happens at the API: a user who manually crafts a request to an endpoint they cannot access receives a clean refusal (`403 Forbidden`). The user interface and the API never disagree on what is allowed — both reference the same authorization rules.

### D.3 No "you don't have permission" tooltips

Permission-related tooltips on every hidden element would clutter the interface and surface implementation details unnecessarily. The system trusts that users granted access to a function know how to use it. Pages they cannot access render an explicit "Access denied" message (section F).

---

## E. Page-by-page visibility matrix

### E.1 Navigation menu (sidebar / topbar)

| Menu item | Field | Office | Supervisor | Admin | +L | Tenant |
| --- | :---: | :---: | :---: | :---: | :---: | :---: |
| Dashboard / Tenants list | ✓ | ✓ | ✓ | ✓ |  | (only their bound tenant) |
| Master Tools | ✓ | ✓ | ✓ | ✓ |  |  |
| Master Calibrations | ✓ | ✓ | ✓ | ✓ |  |  |
| Master Licenses |  |  | ✓ | ✓ | ✓ |  |
| Tenant settings (per-tenant) |  | ✓ | ✓ | ✓ |  |  |
| Tenant member management (per-tenant) |  |  | ✓ | ✓ |  |  |
| Admin → Users |  |  |  | ✓ |  |  |
| Admin → System settings |  |  |  | ✓ |  |  |
| Admin → Audit |  |  |  | ✓ |  |  |
| Sign out | ✓ | ✓ | ✓ | ✓ |  | ✓ |

### E.2 Per-page action visibility — admin pages

#### `/admin/users` (User list)

Admin only. Includes a `+ New user` button.

#### `/admin/users/{id}` (User detail)

Page itself: System Administrator only.

Within the page, action availability depends on the **target user's** UserType:

| Action | When target is Team | When target is Tenant |
| --- | :---: | :---: |
| Edit profile (UserName / Email / Names) | Admin only | Admin OR Office |
| Edit classification (TeamSubtype, Tenant binding) | Admin only | Admin OR Office |
| Reset password | Admin only | Admin OR Office |
| Lock / Unlock account | Admin only | Admin OR Office |
| Change session lifetime | Admin only | Admin OR Office |
| Grant / Revoke admin role | Admin only | (always disabled — Tenant users cannot be admin) |
| Grant / Revoke capability claim | Admin only | (always disabled — Tenant users cannot hold capabilities) |

#### `/admin/users/new` (Create user)

Page itself: visible to Admin and Office. Office users see only the **Tenant** option in the user-type selector — the **Team** option is hidden. Admin users see both.

### E.3 Per-page action visibility — master pages

#### `/tenants` (Tenants list)

Visible to everyone signed in (Team users).

| Button | Visible to |
| --- | --- |
| `+ Provision tenant` | Supervisor, Admin |
| Per-row Deactivate / Reactivate / Archive | Supervisor, Admin |
| Per-row Edit settings | Office, Supervisor, Admin |

#### `/tenants/{code}` (Tenant detail)

Visible to: tenant members, plus Admin.

| Button | Visible to |
| --- | --- |
| Edit settings | Office, Supervisor, Admin |
| Deactivate / Reactivate / Archive | Supervisor, Admin |
| Manage members (link) | Supervisor, Admin |

#### `/tenants/{code}/members` (Tenant members)

Visible to: tenant members, plus Admin. Member list always shown.

| Button | Visible to |
| --- | --- |
| `+ Add member` | Supervisor, Admin |
| Per-row Remove | Supervisor, Admin |

#### `/tools` and `/calibrations` (master fleet)

Visible to everyone signed in (Team users).

| Action | Tools | Calibrations |
| --- | --- | --- |
| `+ New` button | Supervisor, Admin | Office, Supervisor, Admin |
| Per-row Edit | Supervisor, Admin | Office, Supervisor, Admin |
| Per-row Delete | Supervisor, Admin | Office, Supervisor, Admin |

#### `/licenses` (License generation)

Page itself: visible to Supervisor, Admin, and any user holding the **Licensing** capability claim.

All write actions on the page (Generate, Revoke, Download `.lic`, Download key.txt) follow the same gate.

### E.4 Per-page action visibility — tenant data pages

The following pages share a uniform shape:

- Jobs
- Wells
- Tie-Ons
- Surveys
- Tubulars
- Formations
- Common Measures
- Magnetics
- Logs
- Comments

| Action | Field | Office | Supervisor | Admin | Tenant |
| --- | :---: | :---: | :---: | :---: | :---: |
| Read (page + grid) | ✓ | ✓ | ✓ | ✓ | ✓ (bound tenant only) |
| `+ Add new` button |  | ✓ | ✓ | ✓ |  |
| Per-row Edit |  | ✓ | ✓ | ✓ |  |
| Per-row Delete |  | ✓ | ✓ | ✓ |  |

### E.5 Per-page action visibility — Runs and Shots

These two pages have wider write access because Runs are the operational artefact of field engineers' work.

| Action | Field | Office | Supervisor | Admin | Tenant |
| --- | :---: | :---: | :---: | :---: | :---: |
| Read (page + grid) | ✓ | ✓ | ✓ | ✓ | ✓ (bound tenant only) |
| All write actions (`+ Add`, Edit, Delete, bin upload) | ✓ | ✓ | ✓ | ✓ | ✓ (bound tenant only) |

---

## F. Access-denied behavior

When a user navigates directly to a URL their role does not permit (for example, by clicking a link in a bookmark or an email):

- A dedicated **`/forbidden`** page is displayed.
- The page identifies the requested resource and the role required to access it.
- A "Back home" link returns the user to their default landing page.

The user is **not** silently redirected to the home page (this would obscure the cause). They are **not** signed out (their session is still valid for other pages).

When a user clicks an action button they should not see (only possible if the interface fails to gate correctly — a defect), the API returns a `403 Forbidden` with a problem-details body explaining the missing permission. This is the security backstop and should not be reached in normal use.

---

## G. Tenant user experience (interim)

Tenant users — external customer accounts hard-bound to a single tenant — currently navigate the same interface as Team users, with the following differences:

- They see only their bound tenant in the Tenants list (no other tenants are visible).
- All master menus (Tools, Calibrations, Licenses) are hidden.
- All tenant-management menus (settings, members, lifecycle) are hidden.
- The user-administration area is hidden.
- Within their bound tenant, they have **read access** to all data and **write access** to Runs, Shots, and bin uploads.
- They cannot create or modify Jobs, Wells, or other tenant content.

A dedicated Tenant portal (separate controllers, separate page surface) is planned for a future release and will replace this interim arrangement.

---

## H. Acceptance criteria

This SOP is approved when:

1. A representative System Administrator signs in and confirms the admin area renders as documented in section E.2.
2. A representative Supervisor signs in and confirms tenant lifecycle and license actions are visible and operable.
3. A representative Office user signs in and confirms:
   - The "Provision tenant" button is hidden on the Tenants list.
   - The user-creation page hides the "Team" user-type option.
   - Master Tools write actions are hidden; master Calibrations write actions are visible.
4. A representative Field user signs in and confirms only Runs and Shots are writable; all other data is read-only.
5. A representative Tenant user signs in and confirms the navigation matches section G.
6. Each persona attempts to navigate directly to a URL outside their permission set and confirms the `/forbidden` page renders correctly.

---

# Document control

## Revision history

| Version | Date | Author | Changes |
| --- | --- | --- | --- |
| 1.0 (draft) | 2026-05-01 | Mike King (KingOfTheGeeks) | Initial draft. Documents the user-interface gating across all pages and personas, including the new `/forbidden` page, Tenant user interim experience, and per-page action visibility. Pairs with SDI-ENG-SOP-002 (Authorization Redesign). |

## Change-control protocol

Updates to this SOP follow the standard procedure-change rules:

1. Every code change that alters a visible UI element behind a permission gate (a new button, a moved menu, a changed visibility rule) **requires** a corresponding update to the matrix in section E in the same pull request.
2. Adding or removing a page bumps the SOP minor version (1.0 → 1.1). Restructuring section E or adding a new persona bumps the major version (1.x → 2.0).
3. Every SOP version is tagged in source control alongside the Enki release it covers.

This SOP must be kept synchronized with SDI-ENG-SOP-002 (Authorization Redesign) — any policy change in the authorization SOP that affects the user interface requires a matching update here.

## Storage and distribution

The authoritative source of this SOP is the markdown in the Enki repository (`docs/sop-gui-gating.md`). The compiled `.docx` artifact (`docs/sop-gui-gating.docx`) is regenerated from that source at release time and distributed to client end-users, administrators, and onboarding personnel as needed.

Print copies are uncontrolled. The source-of-truth is the version in the repository tagged for the release under review.

## Approval

By signing below, the approver attests that this SOP correctly documents the user interface gating in the corresponding Enki release.

|  | Name | Role | Signature | Date |
| --- | --- | --- | --- | --- |
| Reviewed by |  |  |  |  |
| Approved by |  |  |  |  |
| Client sign-off |  |  |  |  |

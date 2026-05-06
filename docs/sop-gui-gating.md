---
title: "Enki — User Interface Gating"
subtitle: "Standard Operating Procedure"
author: "SDI · KingOfTheGeeks"
date: "2026-05-06"
---

# Enki — User Interface Gating

*Last audited: 2026-05-06 against `main` HEAD `c3b589a`. Sidebar groups + page-level gates verified against `NavMenu.razor` and the per-page `[Authorize]` attributes.*

**Standard Operating Procedure**

| Field | Value |
| --- | --- |
| Document number | SDI-ENG-SOP-003 |
| Version | 1.2 |
| Effective date | 2026-05-01 (v1.0); 2026-05-06 (v1.2 audit + Active) |
| Document owner | Mike King — KingOfTheGeeks |
| Issuing organization | SDI Engineering |
| Status | Active |
| Related repo | <https://github.com/KingOfTheGeeks/Enki> |
| Related commit | `01206c2` (`feat(authz): subtype + capability authorization with parametric policy`) |
| Related SOP | SDI-ENG-SOP-002 (Authorization Redesign) |
| Live at | <https://dev.sdiamr.com/> (since 2026-05-05) |
| Reviewed by | _________________ |
| Approved by | _________________ |

---

## A. Purpose

This Standard Operating Procedure documents how the Enki user interface (BlazorServer host) adapts to each user's role and permissions — what menus, buttons, and form controls each persona will see, and how the application responds when a user navigates to a page they are not authorized to view.

It is intended for client review of the user-interface behavior changes being introduced alongside the authorization redesign (SDI-ENG-SOP-002). It is the authoritative reference for "what will my users see when they sign in?".

## B. Scope

**In scope:** every authorization-sensitive interface element in the BlazorServer web application — navigation menus, page-level access, action buttons that are gated, and the access-denied response when a user reaches a page their role does not permit.

**Out of scope:** field-side desktop tools (Esagila), the licensing-asset packaging tool (Nabu), any external integrations consuming Enki's REST API, and the API-side permission enforcement (which is the security backstop for everything visible in this SOP and is documented in SDI-ENG-SOP-002).

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

### D.2 The user interface is convenience; the API is the security backstop

Hidden buttons are a usability feature. The actual security enforcement happens at the API: a user who manually crafts a request to an endpoint they cannot access receives a clean refusal (`403 Forbidden` with an RFC 7807 problem-details body). The user interface and the API never disagree on the *audience* for an action — both reference the same authorization rules (the named policies documented in SDI-ENG-SOP-002 section I).

In specific cases, the UI deliberately defers to the API backstop rather than gating in the page (see section E.4). This is a documented trade-off, not a defect.

### D.3 No "you don't have permission" tooltips

Permission-related tooltips on every hidden element would clutter the interface and surface implementation details unnecessarily. The system trusts that users granted access to a function know how to use it. Pages that explicitly redirect users without permission go to the dedicated `/forbidden` page (section F).

---

## E. Per-page gating inventory

This section is the authoritative inventory of what is gated where. It distinguishes:

- **Page-level gate** — set via `[Authorize(Policy = …)]` or a runtime `OnInitializedAsync` redirect. A user without the permission cannot load the page.
- **Action-button gate** — set via an `@if (capability)` wrapper around the button. A user without the permission can load the page but does not see the button.
- **Backstop only** — the page renders for any signed-in user; the button submits to an API endpoint that enforces the gate and returns 403 for unauthorized callers.

### E.1 Navigation menu (sidebar)

The sidebar groups items into five sections, ordered by audience reach:

| Group | Items | Visible to |
| --- | --- | --- |
| **Overview** | Home (`/`) | Every signed-in user. |
| **Tenants** | All Tenants (`/tenants`) | Every signed-in Team user; **hidden** for Tenant-type users. |
| Tenants (contextual) | Overview, Jobs, Wells, Runs, Members, Audit (under `/tenants/{code}/…`) | Appears when a tenant is in URL scope. The contextual *Members* sub-link is gated by an async `CanManageTenantMembersAsync({code})` probe; it appears only for Supervisors / admins / users who have membership-management on that tenant. |
| **Fleet** | Tools (`/tools`) | Every signed-in Team user. Tenant users currently see this entry; they have read access to the Tools registry but no action buttons render for them. |
| **Licensing** | Licenses (`/licenses`) | Visible only when `CanManageLicensing` is satisfied: Supervisor+, admin, or holder of the `Licensing` capability claim. Lives in its own group (rather than under SYSTEM) because the audience is broader than admin-only. |
| **System** | Users (`/admin/users`), Settings (`/admin/settings`), Audit (`/admin/audit`) | Whole group hidden for non-admins via `<AuthorizeView Roles="enki-admin">`. |

The contextual sub-tree (Jobs > Wells / Runs > drill-in markers) populates only when a tenant is in URL scope. The Models drill-in is reserved in the parser but not yet rendered (commented out pending the Models pages landing in a future release).

### E.2 Admin pages (the SYSTEM group)

| Page | Page-level gate | Notes |
| --- | --- | --- |
| `/admin/users` | `EnkiAdminOnly` | Lists every user in the system. The list dumps both Team and Tenant users, hence admin-only. |
| `/admin/users/new` | `EnkiAdminOnly` | Office-tier creation of Tenant-type users is planned to surface through the tenant-members workflow rather than this admin page. The Team-radio hide logic in the page is defensive — it is dead code today but correct ahead of any future widening of the page-level policy. |
| `/admin/users/{id}` | `EnkiAdminOnly` | All actions (Edit profile, Reset password, Lock/Unlock, Change session lifetime, Grant/Revoke admin, Capability checkboxes) render unconditionally on the assumption the caller is admin. The per-target authorization rules documented in SDI-ENG-SOP-002 (Office may manage Tenant-type users at the API level) are enforced only through the future tenant-members surface, not on this page. |
| `/admin/settings` | `EnkiAdminOnly` | System-level settings. |
| `/admin/audit` (and child pages: master, identity, auth-events, entity drill-ins) | `EnkiAdminOnly` | Cross-tenant audit feed. The per-tenant audit at `/tenants/{code}/audit` is separately accessible to tenant members. |

### E.3 Master / cross-tenant pages

| Page | Page-level gate | Action gates |
| --- | --- | --- |
| `/tenants` | `[Authorize]` (any signed-in) | `+ Provision tenant` button gated by `CanProvisionTenants`. There are no per-row action buttons on this page; lifecycle and edit live on the detail page. |
| `/tenants/new` | `CanProvisionTenants` | Form-only; no nested gates. |
| `/tenants/{code}` | `[Authorize]` (any signed-in) | Lifecycle buttons (Deactivate/Reactivate) gated by `CanManageTenantLifecycle`; Edit gated by `CanWriteMasterContent`; Members link gated by an async `CanManageTenantMembersAsync({code})` probe with the answer cached for the page lifetime. There is no Archive button — Archive is a terminal status reachable only via direct DB action today. |
| `/tenants/{code}/edit` | `CanWriteMasterContent` | Form-only; no nested gates. |
| `/tenants/{code}/members` | `[Authorize]` (any signed-in) **plus** `OnInitializedAsync` redirect to `/forbidden?required=Supervisor&resource=Members+%2F+{code}` when `CanManageTenantMembersAsync({code})` is false. Member list and add/remove buttons render unconditionally inside the page; the page-level redirect is the gate. |
| `/tools` | `[Authorize]` (any signed-in) | `+ New tool` gated by `CanManageMasterTools`. There are no per-row Edit/Delete buttons on the list page. |
| `/tools/{serial}` | `[Authorize]` (any signed-in) | Edit / Retire / Reactivate gated by `CanManageMasterTools`; `+ Calibrate` gated by `CanWriteMasterContent` (Office can calibrate a tool they cannot otherwise edit). |
| `/tools/{serial}/edit`, `/tools/new` | `CanManageMasterTools` | Form pages; no nested gates. |
| `/tools/{serial}/calibrate` | `CanWriteMasterContent` | Calibration wizard. |
| `/licenses`, `/licenses/new`, `/licenses/{id}` | `CanManageLicensing` | All write actions on the page (Generate, Revoke, Download `.lic`, Download key.txt) are gated by the same page-level policy. |

### E.4 Tenant data pages — current gating coverage

The following pages are inside the per-tenant URL scope (`/tenants/{code}/…`). All have a page-level `[Authorize]` cookie gate; the API enforces the per-action rules by policy.

This release introduces *action-button* gating on the highest-traffic pages. Other tenant-data list and edit pages remain on the API backstop until a follow-up release picks them up.

| Page | Action-button gating today |
| --- | --- |
| `/tenants/{code}/jobs` | `+ New job` gated by `CanWriteTenantContentAsync` (cached). |
| `/tenants/{code}/jobs/{id}` | Lifecycle transitions and Edit gated by `CanWriteTenantContentAsync` (cached). |
| `/tenants/{code}/jobs/{id}/wells` | `+ New well` gated by `CanWriteTenantContentAsync` (cached). |
| All other tenant-data list and detail pages (`Surveys`, `TieOns`, `Tubulars`, `Formations`, `CommonMeasures`, `Magnetics`, `Logs`, `Shots`, `Runs`, the corresponding `*Create` / `*Edit` form pages) | API backstop only this release. Action buttons render for any signed-in user who reaches the page; the API returns 403 if the caller's policy is insufficient. |

For Field users and Tenant users, the API gate means write attempts on the not-yet-gated pages are refused at submit time rather than hidden at render time. This is intentional — the gating is being rolled out incrementally and the user-trust model assumes operators with access to a tenant's pages know which actions they're authorised to perform.

### E.5 Runs and Shots

`Runs` and `Shots` write endpoints are open to any tenant member or admin (subtype-agnostic), so Field users and Tenant users can create them. Page-level: `[Authorize]` (any signed-in), no per-action gating in the page — the API gate is the same as the page audience, so no UI wrapping is needed.

### E.6 Other pages not in any matrix above

| Page | Authorization | Notes |
| --- | --- | --- |
| `/account/settings` | `[Authorize]` | Per-user preferences. Visible to every signed-in user. |
| `/tenants/{code}/audit` | `[Authorize]` (any signed-in) | API enforces tenant-membership via `CanAccessTenant`. Per-tenant audit feed. |
| `/tenants/{code}/jobs/{jobId}/wells/plot` | `[Authorize]` (any signed-in) | Plan-view plot of wells under a job. Read-only. |
| `/tools/{serial}/calibrations/compare` | `[Authorize]` (any signed-in) | Calibration comparison view. Read-only. |
| `/calibrations/{id}` | `[Authorize]` (any signed-in) | Calibration detail page. Reachable by deep-link from a tool. There is no top-level `/calibrations` listing page in this release. |

---

## F. Access-denied behavior

Two distinct flows trigger access denial in the UI:

### F.1 Page-level policy failure (framework default)

When a page declares `[Authorize(Policy = …)]` and the policy is not satisfied, the framework's default unauthorized-result fires. The user sees the framework-provided 401/403 response — there is no automatic redirect to `/forbidden` for this path.

This is the path used by License pages, master Tools edit pages, the admin area, etc. A user navigating directly to one of these URLs without permission gets a standard not-authorized response from the framework rather than a styled application page.

### F.2 Runtime probe failure (explicit `/forbidden` redirect)

When a page runs a tenant-scoped capability probe in `OnInitializedAsync` (currently only `/tenants/{code}/members`), a failed probe explicitly navigates to:

```
/forbidden?required=Supervisor&resource=Members+%2F+{code}
```

The `/forbidden` page renders the requested resource and the role required to access it, with a "Back home" link to the user's default landing page. The user is **not** silently redirected to the home page (this would obscure the cause), and is **not** signed out (their session is still valid for other pages).

A future release will broaden the explicit-redirect path to cover the policy-attribute gates as well, providing a uniform styled denial across the application.

### F.3 Action button submitted by a user who shouldn't have seen it

Possible only if the UI gate failed to render correctly (a defect). The API returns `403 Forbidden` with an RFC 7807 problem-details body explaining the missing permission. This is the security backstop and should not be reached in normal use.

---

## G. Tenant user experience (interim)

Tenant-type users — external customer accounts hard-bound to a single tenant — currently navigate the same interface as Team users, with the following differences enforced by the UI:

- The "All Tenants" link is hidden in the sidebar.
- The Licensing group is hidden (no Licensing capability).
- The System group (Users, Settings, Audit) is hidden (not admin).
- "+ New job" / "+ New well" buttons hide on the gated tenant-data pages because the tenant-scoped write probe fails for Tenant-type users.

The following are **not** UI-gated but are enforced by the API backstop:

- Direct navigation to `/tenants` returns the API's filtered list (the API filters to the bound tenant for Tenant-type callers); the UI does not redirect the URL.
- All tenant-data create / edit pages render for Tenant-type users, but submits to non-Runs/Shots write endpoints are refused with 403 from the API.

A dedicated Tenant portal (separate controllers, separate page surface, no shared admin layout) is planned for a future release and will replace this interim arrangement.

---

## H. Membership probe behaviour

Several gates depend on tenant membership ("am I a member of *this* tenant?"). These are evaluated by an async probe (`CanManageTenantMembersAsync`, `CanWriteTenantContentAsync`, `CanAccessTenantAsync`) that reads from a per-circuit cache. The cache is populated by a single `GET /me/memberships` round-trip on first use within a Blazor circuit.

User-visible consequence: on the first navigation to a per-tenant page within a fresh circuit (i.e., immediately after sign-in), action buttons gated by these probes briefly render hidden until the probe completes — typically a few hundred milliseconds. Subsequent navigations within the same circuit see them immediately.

The default value is "false" while the probe is in flight. This is a deliberate fail-closed choice: better to flash a hidden state than to flash an action that the user cannot perform.

---

## I. Acceptance criteria

This SOP is approved when:

1. A representative System Administrator signs in and confirms the navigation menu shows all five groups (Overview, Tenants, Fleet, Licensing, System) and that every admin-only page in section E.2 is reachable.
2. A representative Supervisor signs in and confirms:
   - Licensing group appears in the sidebar.
   - The System group does not appear.
   - On `/tenants/{code}` for a tenant they are a member of, the Deactivate / Reactivate, Edit, and Members buttons all render.
3. A representative Office user signs in and confirms:
   - The Licensing group does not appear (unless they hold the Licensing capability).
   - On `/tenants` the "+ Provision tenant" button does **not** appear.
   - On `/tenants/{code}` the Deactivate / Reactivate buttons do **not** appear, but Edit does.
   - On `/tools/{serial}` the "+ Calibrate" button appears, but Edit / Retire do not.
   - On `/tenants/{code}/jobs` the "+ New job" button appears.
4. An Office user holding the Licensing capability claim signs in and confirms the Licensing group appears.
5. A representative Field user signs in and confirms:
   - The "+ New job" button is **hidden** on `/tenants/{code}/jobs`.
   - The "+ New well" button is **hidden** on `/tenants/{code}/jobs/{id}/wells`.
   - On `/tenants/{code}/jobs/{id}/runs` they can add Runs (this is the operational-write surface).
6. A representative Tenant user signs in and confirms the navigation matches section G — All Tenants hidden, Licensing hidden, System hidden, contextual tenant block visible for their bound tenant only.
7. A non-member attempts to navigate directly to `/tenants/{code}/members` for a tenant they cannot manage and confirms the `/forbidden` page renders with the requested resource and the required role.

---

# Document control

## Revision history

| Version | Date | Author | Changes |
| --- | --- | --- | --- |
| 1.0 (draft) | 2026-05-01 | Mike King (KingOfTheGeeks) | Initial draft. Documented the user-interface gating across all pages and personas, including the new `/forbidden` page, Tenant user interim experience, and per-page action visibility. |
| 1.1 (draft) | 2026-05-02 | Mike King (KingOfTheGeeks) | Replacement aligned to commit `01206c2`. Replaced the navigation matrix in section E.1 to reflect the actual five-group sidebar (Overview / Tenants / Fleet / Licensing / System). Removed claims of "Master Calibrations" as a top-level menu item (no such page exists). Removed claims of "+ New / Edit / Delete" gating on tenant-data pages other than Jobs and Wells (the rest are API-backstop-only this release). Removed the "Per-row Deactivate / Reactivate / Archive" claim from `/tenants` (no per-row actions on the list page; lifecycle lives on the detail page only; no Archive button anywhere this release). Removed the per-target action table on `/admin/users/{id}` — that flow was deferred to a future tenant-members surface. Added section H documenting the membership probe + per-circuit cache behaviour. Clarified section F that `/forbidden` is opt-in (currently only TenantMembers redirects there) — framework-default response handles `[Authorize(Policy=…)]` failures. Added section E.6 inventory of additional pages (AccountSettings, TenantAudit, WellsPlot, CalibrationCompare, CalibrationDetail). Reworked acceptance criteria to be satisfiable. |
| 1.2 | 2026-05-06 | Mike King (KingOfTheGeeks) | Audit pass against `main` HEAD `c3b589a`. Status promoted Draft → Active — system live on `https://dev.sdiamr.com/` since 2026-05-05. Verified five sidebar groups (Overview / Tenants / Fleet / Licensing / System) against `NavMenu.razor`, the reserved-but-commented-out Models drill-in still present in the parser, and the membership-probe per-circuit cache pattern. No content drift found. |

## Change-control protocol

Updates to this SOP follow the standard procedure-change rules:

1. Every code change that alters a visible UI element behind a permission gate (a new button, a moved menu, a changed visibility rule) **requires** a corresponding update to section E in the same pull request.
2. Adding or removing a top-level page bumps the SOP minor version (1.1 → 1.2). Restructuring section E or adding a new persona bumps the major version (1.x → 2.0).
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

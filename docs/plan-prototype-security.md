---
title: "Enki — Prototype Pre-Deployment Security Plan"
subtitle: "Critical-tier work items for first-customer readiness"
author: "SDI · KingOfTheGeeks"
date: "2026-05-02"
---

# Enki — Prototype Pre-Deployment Security Plan

| Field | Value |
| --- | --- |
| Document number | SDI-ENG-PLAN-001 |
| Document type | Engineering Plan |
| Version | 1.1 |
| Status | Draft |
| Effective date | 2026-05-02 |
| Document owner | Mike King |
| Related docs | SDI-ENG-SOP-002 (Authorization), SDI-ENG-SOP-004 (Security Testing), `docs/Enki-Permissions-Matrix.docx`, `docs/deploy.md` |

---

# 0. Executive summary

This plan covers the **Critical-tier** security work that must complete
before a first prototype customer can sign in to Enki. The original
top-five list from the May 2026 access-control review has been verified
against current code: two items (license-generation audit, admin
role-flip audit) are already implemented and have been removed. The
remaining real work is **three workstreams plus one verification pass**:

| # | Workstream | Effort | Order | Blocks customer access? |
| --- | --- | --- | --- | --- |
| A | Phase 5b auth UX (forgot-password + email confirmation) | 7–10 dev-days | Last (depends on email infra) | Yes — admin-driven password resets don't scale to a paying customer |
| B | File-upload hardening (content-type, magic-byte, filename sanitisation) | 3–5 dev-days | Mid | Yes — exposes shot/log endpoints to malformed binaries |
| C | Secrets management + deploy-guide finalisation (env-vars only for SDI-hosted; Phase 2 customer-hosted left open) | ~2 dev-days | First | Yes — production must not boot from `appsettings.json` |
| D | Sensitive-op audit verification (every privileged op is captured) | 0.5 dev-day | First (parallel with C) | No — defensive, but cheap |

**Total effort:** approximately 12–18 dev-days for one engineer focused.
Splittable across two engineers as workstreams C+D parallel with B,
then both feed into A.

**Out of scope** (explicitly deferred to High-tier or later):
TOTP MFA, centralised logging, rate limiting on tenant-content writes,
force-password-change on first login, idle-session timeout. See
[the prior security review hand-off](#) for the full High-tier list.

\newpage

# 1. Verified-before-planning

Two items from the original Critical list have been verified as already
implemented. They are **out of scope** for this plan and must remain
in place — any regression on them re-promotes them to Critical.

## 1.1 License generation / revocation audit

**Status: implemented.** `License` (in
`src/SDI.Enki.Core/Master/Licensing/License.cs`) implements
`IAuditable`. Every Create / Update / Delete on the entity is captured
by `EnkiMasterDbContext`'s audit interceptor and written to
`MasterAuditLog`. Verified by inspection 2026-05-02.

**Regression detector:** SOP-004 SEC-8.10-003 (Joel generates a
license) followed by a master-audit feed check should show a row.
Add a SOP-004 row asserting this if the regression risk is real.

## 1.2 Admin role-flip audit

**Status: implemented.** `AdminUsersController.SetAdmin`
(`src/SDI.Enki.Identity/Controllers/AdminUsersController.cs:548-591`)
writes a `RoleGranted` / `RoleRevoked` row via `WriteAuditAsync` after
the column flip. The audit row appears in the Identity audit feed
(`/admin/audit/identity`). Verified by inspection 2026-05-02.

**Regression detector:** an admin flips another user's `IsEnkiAdmin`
column → row appears in `/admin/audit/identity`. Worth one SOP-004 row.

\newpage

# 2. Workstream C: Secrets management + deploy guide

**Goal.** A production deployment of Enki must not source any secret
from a committed `appsettings.*.json` file. Connection strings, the
OIDC signing-cert password, the seed user password, and the OIDC client
secret all flow through `IConfiguration`; the production deploy must
wire that abstraction to a vault.

## 2.1 Current state

The three host `Program.cs` files read every secret through
`IConfiguration` already — the abstraction is correct. What's missing
is:

1. **No documented inventory** of every required secret, its
   environment-variable form, and where it's loaded from in production.
2. **`appsettings.Development.json` carries dev secrets** (see
   `Server=localhost;...;Password=5q!@dm1n1str@t0r;...` in
   `src/SDI.Enki.Identity/appsettings.Development.json:14`). These are
   dev-only and must NEVER ship in a release branch's
   `appsettings.{Production}.json` or equivalent.
3. **No startup validation** that required secrets are present in
   non-Development. A missing env var silently falls through to a
   default (sometimes a hard-coded dev fallback), which is dangerous.

## 2.2 Design — phased approach

The deployment scenario informs the choice. Two phases:

**Phase 1 (current):** Enki is hosted on SDI's own servers behind
SDI's firewall. A single ops team controls every host. Secrets
sourcing is uniform: **environment variables** set on each host's
service registration. No vault product. No external dependency.

**Phase 2 (future):** Enki may be deployed to customer-hosted
infrastructure. Customers vary in their secret-management preferences:
some run HashiCorp Vault, some have their own corporate KMS, some
have nothing at all. To avoid locking SDI into a single vault product
(or forcing every customer onto SDI's choice), the architecture stays
deliberately open: customers extend the standard `IConfiguration`
provider chain at deploy time with whatever their environment
supports. SDI's build remains free of any vault dependency.

**Why no vault now, even though SDI controls the infrastructure.**
Running HashiCorp Vault (or equivalent) introduces operational
complexity — sealing/unsealing, backup, HA topology, key recovery
runbooks — before there's a problem that demands it. Env vars give
auditable change history through the deploy scripts (which live in
git). When auditor pressure, rotation requirements, or per-secret
access logs become real needs, that's the moment to introduce a
vault. Until then, simpler is better.

**Provider chain in each host's `Program.cs`** — already wired by
ASP.NET Core defaults, but explicit ordering documented:

```text
1. appsettings.json
2. appsettings.{Environment}.json
3. environment variables (no special prefix)        ← production secret source
4. command-line arguments
```

Customers in Phase 2 plug their provider into step 3 or 4 by editing
their fork's `Program.cs` (or via a partial-class extension hook —
deferred until an actual customer ask).

## 2.3 Tasks

| ID | Task | Files | Depends on | Effort |
| --- | --- | --- | --- | --- |
| C-01 | Inventory every required secret (configuration key, env-var name, what it secures) | grep across `src/`; cross-check `appsettings.*.json` | — | 0.5d |
| C-02 | Confirm `AddEnvironmentVariables` is in each host's config chain (probably already is via ASP.NET defaults — verify) | `src/*/Program.cs` (3 files) | — | 0.25d |
| C-03 | Add startup validation: fail loud in non-Development when any required secret is missing | `src/*/Program.cs` | C-01 | 0.5d |
| C-04 | Rewrite `docs/deploy.md` Secret staging section: env-var contract, secret inventory table, Phase 2 future-proofing notes | `docs/deploy.md` | C-01 | 0.5d |
| C-05 | Smoke test: each host boots with all secrets in env vars and zero secrets in `appsettings.*.json` | scripted in `scripts/` | C-02, C-03 | 0.25d |

## 2.4 Test strategy

- **Unit:** trivial — no business logic added.
- **Integration:** the smoke test in C-05 is the meaningful coverage:
  `appsettings.Production.json` carries no secrets, all values come
  from env vars, host boots cleanly. Re-run on every release.
- **Negative:** delete a required env var, boot host, confirm it fails
  loud at startup (with the missing key name in the message), rather
  than silently falling through to a default or a dev fallback.

## 2.5 Risks and unknowns

- **Dev seed fallback exposure.** `Identity:Seed:DefaultUserPassword`
  carries the dev fallback `Enki!dev1` in the seeder. The C-03 startup
  validation must reject any path where this fallback is reachable in
  non-Development — otherwise a customer install with the seed env
  var unset could ship a host with a known password.
- **Phase 2 customer extensibility — deferred, not solved.** Customers
  who want their own vault will fork `Program.cs` to add their
  provider. This is acceptable for the foreseeable future; if customer
  demand consolidates around one or two patterns (e.g. several
  customers with HashiCorp Vault), introduce a partial-class
  `ApplyCustomerProviders` hook at that point.

## 2.6 Acceptance criteria

- A1: Every secret in C-01's inventory has an entry in `docs/deploy.md`.
- A2: All three hosts boot in a Production environment with zero
  secrets in `appsettings.*.json`.
- A3: Removing any required secret produces a fail-loud startup error,
  not a silent fall-through.
- A4: `docs/deploy.md` reviewed and signed off by the Document Owner.

\newpage

# 3. Workstream D: Sensitive-op audit verification

**Goal.** Every privileged operation in the system writes a row to one
of the audit feeds (master / identity / auth-events). This is a
verification pass, not new development — known-implemented ops were
proven during the May 2026 review (license generation, admin role
flip), but a comprehensive sweep has never been done.

## 3.1 Approach

For every controller action gated by a non-`EnkiApiScope` policy
(i.e. every privileged operation), confirm one of:

- The action's entity implements `IAuditable` (interceptor-captured); OR
- The action explicitly calls `WriteAuditAsync` / equivalent; OR
- The action is read-only and intentionally not audited.

Document the sweep result in a table; raise issues for gaps.

## 3.2 Tasks

| ID | Task | Effort |
| --- | --- | --- |
| D-01 | Enumerate every controller action gated by a non-`EnkiApiScope` policy | 0.25d |
| D-02 | Categorise each action (interceptor-captured / explicit-audit / read-only) | 0.25d |
| D-03 | File issues for any privileged write that is NOT captured | 0d (deliverable) |

## 3.3 Acceptance criteria

- A1: A table is committed to `docs/audit-coverage-matrix.md` listing
  every privileged action and its audit shape.
- A2: Any gap identified in D-03 is either fixed or filed as an
  accepted-known-issue with a tracked backlog item.

## 3.4 Risks

Likely outcome: zero gaps. The `IAuditable` + interceptor pattern is
broadly applied. The cost of confirming this is small (half a day) and
the benefit is a defensible audit-coverage statement when a customer
auditor asks.

\newpage

# 4. Workstream B: File-upload hardening

**Goal.** Every binary upload endpoint must reject malformed,
oversized, or hostile uploads before the bytes reach domain code or
storage. Today size limits are enforced; content-type, magic-byte
verification, and filename sanitisation are not.

## 4.1 Endpoints in scope

Four binary-upload endpoints at the WebApi layer:

| Endpoint | Verb | Controller | Notes |
| --- | --- | --- | --- |
| `/tenants/{c}/jobs/{j}/runs/{r}/shots/{s}/binary` | POST | `ShotsController.UploadBinary` | Primary tool binary, ≤250 KB |
| `/tenants/{c}/jobs/{j}/runs/{r}/shots/{s}/gyro-binary` | POST | `ShotsController.UploadGyroBinary` | Gyro tool binary |
| `/tenants/{c}/jobs/{j}/runs/{r}/passive/binary` | POST | `RunsController.UploadPassiveBinary` | Passive run binary |
| `/tenants/{c}/jobs/{j}/runs/{r}/logs/{l}/binary` | POST | `LogsController.UploadBinary` | Log capture binary |

## 4.2 Current state

For each endpoint:

| Hardening | Status |
| --- | --- |
| `RequestSizeLimit` attribute | ✓ enforced (per-endpoint cap) |
| Explicit length check (defence-in-depth) | ✓ on Shots; verify on Runs/Logs |
| `ContentType` allow-list | ✗ accepts any content type |
| Magic-byte / signature verification | ✗ not validated |
| Filename sanitisation | ✗ filename stored verbatim from `IFormFile.FileName` |
| Storage isolation | ✓ stored as varbinary in tenant DB (no filesystem path) |
| Audit on upload | partial — uploads are EF-tracked (interceptor catches the row mutation) but no explicit "binary uploaded" event |

## 4.3 Design

**Layer 1 — request-shape validation** (per controller action):
- Reject if `IFormFile.Length == 0` (already done on Shots).
- Reject if `Length > MaxBinaryBytes` (already done).
- Reject if `IFormFile.ContentType` not in `{ "application/octet-stream" }` (the AMR binary class). New.
- Reject if `IFormFile.FileName` contains path separators (`/`, `\`),
  control characters, or NUL. New.

**Layer 2 — content validation** (centralised):
- A `BinaryFormatValidator` service inspects the first N bytes against
  a known-good signature for the AMR binary format. Reject on
  mismatch.
- The signature is provided by the Marduk team (or extracted from a
  representative sample) — discovery item.

**Layer 3 — storage:**
- Use `Path.GetFileName(file.FileName)` defensively even though we
  don't write to disk, so any future filesystem migration doesn't
  silently re-introduce the gap.

**Out of scope:**
- Antivirus scanning. Mark as a High-tier item; route through
  ICAP or a cloud AV when customer compliance demands it.
- Re-encoding / format conversion server-side.

## 4.4 Tasks

| ID | Task | Files | Effort |
| --- | --- | --- | --- |
| B-01 | Confirm length-check on `RunsController.UploadPassiveBinary` and `LogsController.UploadBinary` matches Shots pattern; add if missing | `RunsController.cs`, `LogsController.cs` | 0.5d |
| B-02 | Centralise content-type allow-list as a constant in `EnkiBinaryUploadDefaults` (Shared) | new file in `SDI.Enki.Shared` | 0.25d |
| B-03 | Add content-type guard to all four endpoints | each controller | 0.5d |
| B-04 | Filename sanitiser utility (single-source) | `SDI.Enki.Shared` | 0.25d |
| B-05 | Apply sanitiser to all four endpoints | each controller | 0.5d |
| B-06 | Discovery: obtain the AMR binary-format magic-byte signature from Marduk | conversation + sample bytes | 0.25d |
| B-07 | Implement `BinaryFormatValidator` service + DI registration | `SDI.Enki.Infrastructure` | 0.5d |
| B-08 | Wire validator into all four endpoints | each controller | 0.5d |
| B-09 | Test coverage: ContentType reject, oversized reject, magic-byte reject, filename sanitisation | `tests/SDI.Enki.WebApi.Tests/Controllers/*` | 1.0d |
| B-10 | SOP-004 update: add binary-upload negative-path tests under §8.8 | `docs/sop-security-testing.md` | 0.25d |

## 4.5 Test strategy

- **Unit:** ContentType allow-list, filename sanitiser, BinaryFormatValidator.
- **Integration:** Each upload endpoint is exercised with: valid binary (passes), wrong ContentType (415), oversized (413), wrong magic bytes (400), filename with `/` (400).
- **Manual:** SOP-004 §8.8 gains 4 new rows under `Field+Tenant rig writes` (or a dedicated subsection if more).

## 4.6 Risks

- **Magic-byte unknown.** B-06 might surface that the AMR binary format
  doesn't have a stable header — in which case fall back to length +
  content-type validation only, and document the limitation.
- **Backward compatibility.** Older binaries already in tenant DBs may
  not match the new format check. The validator only runs on uploads,
  not on existing rows.

## 4.7 Acceptance criteria

- A1: Every endpoint in §4.1 rejects each of: empty file, oversized,
  bad ContentType, bad filename, bad magic bytes.
- A2: Test suite covers all 4 negative paths × 4 endpoints (16 new tests).
- A3: SOP-004 §8.8 reflects the new tests.
- A4: Manual penetration probe (see Workstream A in High-tier) confirms
  no untested gap.

\newpage

# 5. Workstream A: Phase 5b auth UX

**Goal.** Customers must be able to recover access without contacting
SDI. This requires a self-service password-reset flow and an
email-confirmation flow. Both depend on a working email service.

## 5.1 Current state

ASP.NET Identity in the Identity host already supports the underlying
primitives (`UserManager.GeneratePasswordResetTokenAsync`,
`GenerateEmailConfirmationTokenAsync`). What's missing:

1. **No `IEmailSender` implementation.** Identity's default no-op
   sender silently swallows email requests.
2. **No public Razor pages** for `/Account/ForgotPassword`,
   `/Account/ResetPassword?token=…`, `/Account/ConfirmEmail?token=…`,
   `/Account/ResendConfirmation`.
3. **Admin-created users have `EmailConfirmed = true`** (verify in
   `IdentitySeedData` and `AdminUsersController.Create`). Customers
   never proved their email.
4. **No SMTP configuration** in `appsettings.*.json`.
5. **No rate limiting** on the new public endpoints (open spam target).

## 5.2 Design

### 5.2.1 Email service

Abstraction: `IEnkiEmailSender` with one method:

```csharp
Task SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken ct);
```

Two implementations:
- `SmtpEnkiEmailSender` — production, configured by `Email:Smtp:*` keys
  (host, port, credentials from vault per Workstream C).
- `LogOnlyEnkiEmailSender` — Development. Writes the email body to
  Serilog and a file under `logs/dev-emails/`. Lets `dotnet test` and
  the dev rig exercise the flow without spamming.

DI: registered via `AddEnkiEmailSender()` extension that picks the
implementation based on environment. Followed pattern from existing
`AddEnkiInfrastructure`.

### 5.2.2 Forgot-password flow

**Public Razor pages on the Identity host:**

- `GET /Account/ForgotPassword` — form with email field.
- `POST /Account/ForgotPassword` — accept email, look up user. **Always
  return the same success page** regardless of whether the email
  exists (prevents email enumeration). If user exists and their
  `EmailConfirmed = true`, generate a token via
  `UserManager.GeneratePasswordResetTokenAsync` and email a link
  containing it.
- `GET /Account/ResetPassword?email=…&token=…` — form with two new-password fields.
- `POST /Account/ResetPassword` — accept submission, call
  `UserManager.ResetPasswordAsync`, force security-stamp rotation
  (invalidates other sessions), redirect to `/Account/Login` with a
  success banner.

**Token TTL.** Identity's default is 24 hours; set explicitly via
`DataProtectionTokenProviderOptions.TokenLifespan` for clarity.

**Rate limiting.** Add to the existing OIDC rate-limit policy:
`/Account/ForgotPassword` is partitioned per-IP and per-email,
allowing 3 requests / 5 min / partition.

**Audit.**
- `PasswordResetRequested` row written on every form submission
  (regardless of whether the email exists, for forensics).
- `PasswordResetCompleted` on success.

### 5.2.3 Email-confirmation flow

**Initial-state policy:**
- New users created by admin: `EmailConfirmed = false`.
- The user can sign in immediately (don't block initial sign-in — the
  admin handed off a temp password and the user has to set their own).
- A persistent banner on the home page reads "Please confirm your
  email — Send confirmation link". Clicking that calls
  `POST /Account/ResendConfirmation` (signed-in only).
- Forgot-password flow is **denied** for users with
  `EmailConfirmed = false` (return the same generic success page so
  there's no enumeration leak, but no email is sent).

**Public Razor page on the Identity host:**

- `GET /Account/ConfirmEmail?userId=…&token=…` — calls
  `UserManager.ConfirmEmailAsync`, redirects to `/Account/Login` with
  a success banner.

**Authenticated endpoint (Identity host):**

- `POST /me/resend-confirmation` (or `/Account/ResendConfirmation`) —
  any signed-in user can request a resend for their own email. Rate
  limited at 1 / 60 sec / user.

**Audit.**
- `EmailConfirmationRequested` on resend.
- `EmailConfirmed` on success.

**Seed migration.** `IdentitySeedData` stamps every seeded user with
`EmailConfirmed = true` so dev workflows are unaffected.

### 5.2.4 First-login UX

Add a banner check in the Blazor home page:
- If `User.HasClaim("email_verified", "false")` (claim materialised
  by `EnkiUserClaimsPrincipalFactory`), render the banner.
- The banner is dismissible per session but reappears on next sign-in
  until confirmed.

## 5.3 Tasks

| ID | Task | Files | Depends on | Effort |
| --- | --- | --- | --- | --- |
| A-01 | `IEnkiEmailSender` interface | `SDI.Enki.Shared` | — | 0.25d |
| A-02 | `SmtpEnkiEmailSender` implementation | `SDI.Enki.Identity` | C-01 (vault for SMTP creds) | 0.5d |
| A-03 | `LogOnlyEnkiEmailSender` (dev) + DI selector | `SDI.Enki.Identity` | A-01 | 0.25d |
| A-04 | `IEmailSender` ASP.NET Identity adapter (delegates to `IEnkiEmailSender`) | `SDI.Enki.Identity` | A-01 | 0.25d |
| A-05 | SMTP configuration keys in `appsettings.json` (template) and `deploy.md` | both | C-04 | 0.25d |
| A-06 | Razor page: `/Account/ForgotPassword` (GET + POST) | `SDI.Enki.Identity/Pages/Account/` | A-02 | 0.5d |
| A-07 | Razor page: `/Account/ResetPassword` (GET + POST) | `SDI.Enki.Identity/Pages/Account/` | — | 0.5d |
| A-08 | Email template (HTML + text) for password reset | `SDI.Enki.Identity/EmailTemplates/` | A-01 | 0.25d |
| A-09 | Rate limit on `/Account/ForgotPassword` (per-IP and per-email) | `SDI.Enki.Identity/Program.cs` | — | 0.25d |
| A-10 | Audit rows for `PasswordResetRequested` / `PasswordResetCompleted` | `SDI.Enki.Identity/Data/AuthEventLogger` | — | 0.25d |
| A-11 | Razor page: `/Account/ConfirmEmail` | `SDI.Enki.Identity/Pages/Account/` | A-02 | 0.25d |
| A-12 | Endpoint: `POST /me/resend-confirmation` | `SDI.Enki.Identity/Controllers/MeController.cs` | A-02 | 0.25d |
| A-13 | Email template for confirmation | `SDI.Enki.Identity/EmailTemplates/` | — | 0.25d |
| A-14 | Audit rows for `EmailConfirmationRequested` / `EmailConfirmed` | as A-10 | — | 0.25d |
| A-15 | `EnkiUserClaimsPrincipalFactory` projects `email_verified` claim | `SDI.Enki.Identity/Data/EnkiUserClaimsPrincipalFactory.cs` | — | 0.25d |
| A-16 | Blazor home-page banner for unconfirmed email | `SDI.Enki.BlazorServer/Components/Pages/Home.razor` | A-15 | 0.5d |
| A-17 | Banner on Blazor calls `POST /me/resend-confirmation` via the existing `EnkiIdentity` HttpClient | as A-16 | A-12 | 0.25d |
| A-18 | Update seeders: `IdentitySeedData` sets `EmailConfirmed = true` for dev users | `SDI.Enki.Identity/Data/IdentitySeedData.cs` | — | 0.25d |
| A-19 | Update `AdminUsersController.Create`: new users get `EmailConfirmed = false` | `SDI.Enki.Identity/Controllers/AdminUsersController.cs` | — | 0.25d |
| A-20 | Integration tests: full forgot-password flow (request, click link, set password, sign in) | `tests/SDI.Enki.Identity.Tests/` | A-06, A-07 | 1.0d |
| A-21 | Integration tests: email confirmation flow | `tests/SDI.Enki.Identity.Tests/` | A-11, A-12 | 0.5d |
| A-22 | Integration tests: enumeration-leak prevention (forgot-password returns same page for known/unknown email) | `tests/SDI.Enki.Identity.Tests/` | A-06 | 0.5d |
| A-23 | SOP-004 update: new §8.17 covering forgot-password, §8.18 covering email confirmation | `docs/sop-security-testing.md` | A-22 | 0.5d |

## 5.4 Test strategy

**Unit:**
- Email service contract (mock SMTP, verify message shape).
- Token TTL enforced.
- Filename sanitiser (cross-cutting).

**Integration (Identity.Tests project):**
- Forgot-password happy path.
- Forgot-password to unknown email returns success page (no enumeration).
- Forgot-password to unconfirmed-email user returns success page, no email sent.
- Reset-password with expired token returns error.
- Reset-password with token from a different user returns error.
- Email-confirmation happy path.
- Resend-confirmation rate limit (>1/min returns 429).
- Forgot-password rate limit (>3/5min returns 429).

**Manual (SOP-004):**
- New §8.17 — forgot-password.
- New §8.18 — email confirmation.

## 5.5 Risks

- **Email deliverability.** SMTP-from-customer-server may land in spam.
  For prototype, accept that. Long-term: SPF/DKIM/DMARC records on the
  customer's domain, or route through a transactional email provider
  (SendGrid, Postmark, etc.). Track as a Medium-tier item.
- **Token-leak via referrer header.** Reset-password URLs carry the
  token in the query string. If the user clicks an external link from
  the reset page, the referrer leaks the token. Mitigation: set
  `Referrer-Policy: no-referrer` on the reset page response.
- **Existing sessions on other devices.** When a password is reset,
  the security stamp rotates; OpenIddict tokens issued for that user
  become invalid on the next API call (returns 401). The user is
  bounced to login. This is the desired behaviour — document it in
  release notes.

## 5.6 Acceptance criteria

- A1: A user can complete the forgot-password flow end-to-end without
  contacting SDI.
- A2: A user can confirm their email after admin-created account.
- A3: All integration tests in §5.4 pass.
- A4: SOP-004 §8.17 + §8.18 added; pass on first run against the
  delivered build.
- A5: No email-enumeration leak (test §5.4 confirms).

\newpage

# 6. Sequencing

```
Day 0 ───► Workstream C (secrets) ──► Workstream D (audit verify)
                │                            │
                ▼                            │
            (vault key for SMTP)             │
                │                            │
                ▼                            ▼
            Workstream A (auth UX) ◄──── Workstream B (uploads)
                                            (parallel)
                │
                ▼
           SOP-004 update + run ──► sign-off
```

**Why this order:**

- **C first** because secrets management is decoupled from feature
  code — finishing it early lets later workstreams use vault-sourced
  values from day 1, especially the SMTP credentials Workstream A
  needs.
- **D in parallel with C** because the audit-coverage sweep is
  read-only across the codebase — no merge conflicts with C's host
  startup edits.
- **B in parallel with A** once C is done. B has zero dependencies
  on A; the two engineers can split.
- **A last (or final)** because it's the largest workstream, depends
  on email infra (which depends on C's vault for SMTP creds), and
  the integration tests need the most stable foundation.

## 6.1 Critical path

The earliest feasible "ready for first prototype customer" date is:
- Single engineer: **C → D → B → A**, ~14 dev-days serial.
- Two engineers: **{C+D} | B → A** with the second engineer starting B
  on day 2 (after C finishes), ~10 dev-days end-to-end.

## 6.2 Definition of done for "prototype-ready"

When all four workstreams' acceptance criteria are met, plus:

- A full SOP-004 run (now expanded with §8.17 + §8.18 + 16 new
  upload-rejection rows) is recorded against the candidate build with
  zero Fail rows.
- A pen-test sweep (out-of-band, ~1 dev-day) confirms no obvious gap.
- The deploy guide (`docs/deploy.md`) documents every secret and the
  vault key path for the chosen target.

\newpage

# 7. Tracking

Each task ID in this plan (e.g. `A-07`, `B-04`, `C-03`, `D-01`) becomes
a GitHub issue with the prefix in the title. Issues link back to this
document. The plan version increments each time the task list
changes; new versions are tagged in source control alongside the
release that consumed them.

| Tracking artefact | Where |
| --- | --- |
| Master plan (this document) | `docs/plan-prototype-security.md` |
| Per-task issues | GitHub Issues, prefixed `[SEC-PLAN] {task-id}: {title}` |
| Release tag for "prototype-ready" | `v0.X-prototype-ready` |
| SOP-004 expanded run record | `docs/test-runs/{date}-{commit}-sec.md` |

# 8. Out of scope (deferred)

These items came up during the May 2026 review but are deferred to
High-tier work, after this plan completes. Listed here so the trail of
decisions is visible:

- TOTP MFA (at minimum for `IsEnkiAdmin` users).
- Centralised log shipping (Seq / Application Insights / ELK).
- Rate limiting on tenant-content writes (DoS surface on
  `/tenants/{c}/runs/{r}/shots/{s}/binary`).
- Force password change on first sign-in.
- Idle session timeout.
- CSP / HSTS / CORS final lockdown.
- Append-only audit log (hash-chain or off-DB shipping).
- Anomaly detection on auth events.
- Bug-bounty / responsible disclosure programme.

# 9. Document control

| Version | Date | Author | Changes |
| --- | --- | --- | --- |
| 1.0 | 2026-05-02 | Mike King | Initial issue. Scoped against the May 2026 access-control review with two original Critical items (license-audit, admin-flip-audit) verified as already implemented and removed from the plan. |
| 1.1 | 2026-05-02 | Mike King | Workstream C revised. Decision: SDI hosts on its own infrastructure for the foreseeable future, with possible migration to customer-hosted environments later. Vault-target selection collapsed — Phase 1 uses environment variables only (no vault product, no external dependency); Phase 2 (customer-hosted) leaves the door open for customers to plug their own secret-management tooling into the standard `IConfiguration` chain. C-02 and C-03 retired; effort estimate dropped from 1–2d to ~2d total. |

---

*End of plan.*

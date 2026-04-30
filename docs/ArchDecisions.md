# Enki Architecture Decisions

Trade-offs the codebase silently bakes in. If you're considering a
refactor that contradicts one of these, read the decision first — most
of them have already been considered and rejected on purpose.

This is not a strict ADR log; the rationale matters more than the
ceremony. Each entry covers what we picked, what we rejected, and why
the rejected option is more attractive than it looks.

---

## 1. Controllers talk to `DbContext` directly. No MediatR. No CQRS.

**Status:** Adopted. Reaffirmed in the 2026-04-24 architecture review.

**Decision:** REST controllers in `SDI.Enki.WebApi` open a tenant
`DbContext` via `ITenantDbContextFactory`, run the query inline, and
return a DTO. There is no command/query bus, no MediatR pipeline, no
Specification objects, no separate handler layer.

**Rejected:** MediatR + handlers + a request/response object per
endpoint. The pattern is widely adopted and it does buy a
testability seam — the handler is a plain class, not coupled to ASP.NET.

**Why we picked direct:**
- The team is small. The indirection's biggest payoff is in codebases
  with many engineers concurrently touching the same controllers; we
  don't have that.
- Controllers are already thin — the largest is `JobsController` at
  ~200 lines, half of which is XML doc. There's no business logic
  hiding in them.
- Tests cover behaviour through `WebApplicationFactory<Program>`,
  which exercises the full pipeline (auth, validation, DB). A handler
  unit test wouldn't add coverage; it'd add an alternate target that
  could drift.
- Adding MediatR later is mechanical (one PR per controller). Removing
  it is not. We ship the simple shape now and revisit if a use case
  for it actually arrives — typically a third party reusing the
  business logic outside HTTP.

**Where this hurts:** anaemic domain model. Validation lives partly
in DataAnnotations on DTOs, partly in entity constructors, partly in
controller bodies. A real domain layer with rich aggregates would
collect this in one place. Tracked as tech debt (prior review #15);
not blocking.

---

## 2. Flat tenancy. No parent/subsidiary hierarchy.

**Status:** Adopted.

**Decision:** A `Tenant` is one row in the master DB pointing at one
pair of per-tenant databases. There is no parent-child relationship
between tenants.

**Rejected:** A hierarchical tenancy model where a parent company
contains subsidiaries, and admin scopes flow down. Multi-corp drilling
operators do exist.

**Why flat:**
- SDI's actual customer shape is one company per tenancy. The
  hierarchy was speculative.
- Cross-tenant data sharing has no demand today. When it does, it'll
  be specific (one well referenced by two tenants), not general
  (one tenant inheriting another's role grants).
- Adding a parent FK later is a single migration. Adding the
  hierarchy now would touch every authorization handler and every
  admin grid.

---

## 3. DB-per-tenant, not table-per-tenant or table-per-job.

**Status:** Adopted. Replaces legacy Athena's per-job DB layout.

**Decision:** Each tenant gets its own pair of databases (Active +
Archive). Cross-tenant queries are impossible by construction.

**Rejected — table-per-tenant:** one shared schema with a `TenantId`
column on every row. Lower ops cost (one DB to back up).

**Rejected — table-per-job (legacy Athena):** every job got its own
DB. Maximal isolation; impossible to keep schemas in sync; backup
nightmare.

**Why DB-per-tenant:**
- Isolation answer is binary: a tenant can never see another's data
  because the connection literally targets a different database. No
  query can leak past a `TenantId` filter, because there is no
  shared table to filter.
- Schema drift is bounded — every tenant DB runs the same migration
  set via `TenantProvisioningService`. Athena's per-job approach
  meant a hotfix had to patch hundreds of databases; tenant-per-DB
  caps it at the customer count.
- DR / restore is per-tenant, which matches the legal scope most
  customers want.

**Where this hurts:** cross-tenant admin operations need a fan-out
(open every tenant DB in turn). System reporting will eventually want
a warehouse fed by per-tenant exports; not a problem today.

---

## 4. Identity host is self-contained. No `SDI.Enki.Core` reference.

**Status:** Adopted.

**Decision:** `SDI.Enki.Identity.csproj` references only
`SDI.Enki.Shared`. It does not reference `SDI.Enki.Core`,
`SDI.Enki.Infrastructure`, or `SDI.Enki.WebApi`.

**Why:** Identity is a generic OIDC server with a tiny set of admin
endpoints. Pulling in Core would drag every domain entity through the
build, blow up the deployment artefact, and tempt accidental coupling.
The two endpoints that need domain types (`MeController` validating
`UnitSystem`, `AdminUsersController` toggling `IsEnkiAdmin`) accept
small drift over the project ref — `MeController` keeps an inline
`"Field" or "Metric" or "SI"` allowlist; if a fourth preset is added,
the test suite catches the gap.

---

## 5. `IsEnkiAdmin` column is the source of truth; the role claim is derived.

**Status:** Adopted in PR 3 (post-Phase 8 follow-up).

**Decision:** `ApplicationUser.IsEnkiAdmin` is authoritative.
`EnkiUserClaimsPrincipalFactory` materialises a `role=enki-admin`
claim from the column at sign-in. The claim is never persisted in
`AspNetUserClaims`.

**Rejected — Option A (claims-only):** delete the column entirely;
use `IdentityRole("enki-admin")` + `UserRoles`. More idiomatic for
ASP.NET Identity.

**Why column-authoritative:**
- Token issuance reads `ApplicationUser` directly in
  `AuthorizationController.Authorize`. A boolean column on the user
  is one read; a role lookup is a join.
- The admin grid (`AdminUsersController.List`) is paginated; reading
  a column scales naturally with paging. Resolving roles per row
  would need a `GetUsersInRoleAsync`-style join.
- The column is what every existing reader already projects.

The previous shape persisted both the column and a claim row. Two
sources of truth means desync is one-deploy away. PR 3 collapsed
them.

---

## 6. Single `EnkiAdmin` policy, not a hierarchy of admin scopes.

**Status:** Adopted.

**Decision:** There is one cross-tenant admin role, `enki-admin`, and
one policy that gates on it. Tenant admins are a separate concept
(`TenantUser.Role == Admin`) covered by `CanManageTenantMembers`.

**Rejected:** Fine-grained admin scopes (`enki-billing`,
`enki-support`, `enki-readonly-admin`).

**Why:** the admin surface is small (~10 endpoints). The set of
people who get `enki-admin` is the set of SDI engineers operating
the platform; subdividing them is theatre. Add a new role when an
actual permission split is needed — not preemptively.

---

## 7. RFC 7807 ProblemDetails everywhere; no anonymous-object error bodies.

**Status:** Adopted. PR 2 closed the last bare-JSON outliers.

**Decision:** Every non-success response carries a ProblemDetails
body. Controllers use `ControllerBase.Problem(...)` /
`ValidationProblem(ModelState)` rather than `BadRequest(new {
error = "..." })`. The Identity host registers
`AddProblemDetails()` so the bodies pick up `traceId` and the
request URI automatically.

**Why:** the Blazor client has one error renderer that knows how to
parse ProblemDetails. Bare-JSON 4xx responses bypass it and surface
as "Unexpected error" with no actionable text. Uniformity here is
worth the small ceremony of `Problem(detail:..., statusCode:...)`.

---

## 8. SmartEnum parsing lives in `SDI.Enki.Core.Abstractions`, not in controllers.

**Status:** Adopted in PR 4.

**Decision:** `SmartEnumExtensions.TryFromName<T>` is the single
parser controllers call when accepting a SmartEnum value off the
wire. `UnknownNameMessage<T>` generates the error text from
`SmartEnum<T>.List`. Adding a SmartEnum value automatically widens
the accepted set + every error message.

**Rejected:** the previous shape — a private `TryParseXxx` helper
in each of four controllers, plus a hard-coded `"Expected A, B, C."`
string per call site. Each helper had a `null!` suppression in its
false branch; lying to the compiler about reference-not-null was
one of the quiet smells the consolidation wiped out.

---

## 9. No global exception filter for domain exceptions; explicit `EnkiException` subclasses + `EnkiExceptionHandler`.

**Status:** Adopted.

**Decision:** `EnkiException` and its subclasses
(`EnkiNotFoundException`, `EnkiConflictException`,
`EnkiValidationException`) carry the structured failure data;
`EnkiExceptionHandler` (registered via `AddExceptionHandler<>`)
maps them to ProblemDetails. Unhandled exceptions reach the same
handler and become 500s with a stable shape.

**Rejected:** an `IExceptionFilter` per HTTP status. Filters fire
late in the pipeline (after model binding, before some middleware);
the global handler runs around everything, which is what we want
for consistent logging + correlation IDs.

---

## 10. Reset-and-recreate is the dev cleanup; no in-place fix-up scripts.

**Status:** Adopted.

**Decision:** `scripts/reset-dev.ps1` drops every Enki database. The
hosts re-apply migrations and re-seed on next boot. There are no
"fix the dev DB" scripts.

**Why:** dev data is disposable. A repair script tempts use against
production. The host startup paths (Identity migrate + retry on SQL
2714, WebApi master migrate + recreate on 2714, tenant provisioning
hooked to `TenantsController.Provision`) handle every state we
actually hit. If they don't, reset and start over — debugging a
half-applied schema fork costs more than five minutes.

---

## 11. Audit capture is two-phase and best-effort; no transaction wraps the entity write.

**Status:** Adopted.

**Decision:** `TenantDbContext.SaveChangesAsync` (and the matching
override in `EnkiMasterDbContext`) runs in two phases:

1. **Phase 1** — stamp `IAuditable` columns (`UpdatedAt`,
   `UpdatedBy`), snapshot pre-save state for every Modified / Deleted
   entity, queue the audit-row metadata.
2. **Phase 1b** — call `base.SaveChangesAsync` so the underlying
   mutation persists. Int-IDENTITY keys are populated here, which is
   why Phase 2 has to come *after* this call rather than before.
3. **Phase 2** — build `AuditLog` rows with the now-real EntityIds
   and call `base.SaveChangesAsync` a second time. Failures are
   caught, logged at Warning, and orphan audit entries are detached
   from the change tracker. The original mutation is **not** rolled
   back.

There is no transaction wrapping Phase 1b + Phase 2.

**Rejected — single-transaction capture:** wrap both saves in a
`BeginTransaction` / `Commit`. Atomic, no possibility of an entity
landing without its audit row. This is what the first cut shipped.

**Why two-phase + best-effort:**

- SQL Server's `SqlServerRetryingExecutionStrategy` (which we keep
  enabled for transient-fault resilience on tenant DBs) **does not
  support user-initiated transactions**. Any explicit
  `BeginTransaction` throws on the first retryable failure. The
  retry strategy is more valuable than the transaction — losing it
  to make audit atomic would degrade reliability everywhere else.
- Audit is a *side ledger*, not part of the business write. If a
  Job update succeeds and the audit row fails, the Job's data is
  still correct — we lose visibility into one mutation, not the
  mutation itself. The opposite ordering (audit first, then entity)
  would be worse: an audit row referring to data that didn't land.
- Mirrors the pattern already in use for `IAuthEventLogger` and
  `IAuthzDenialAuditor` — auth-side observability is also
  log-and-swallow, for the same reasons.
- Operationally: Phase 2 failures are logged with the entity type +
  ID. A persistent failure is a Sev-2 ops issue, not a data-loss
  event.

**Where this hurts:** in theory, a process crash *between* Phase 1b
and Phase 2 would persist the entity without the audit row.
Tolerated because (a) the audit table is a ledger, not a source of
truth; (b) the alternative is losing retry-on-transient-failure
across every tenant DB write.

**Read-side companion:** the per-entity audit tile
(`AuditHistoryTile.razor`) follows a "smallest-grouping" rule —
each tile rolls up only the descendants that don't already own a
detail page with their own audit tile. Wells and Runs fan out to
their immediate audit-emitting children; Job and Tenant tiles are
entity-only because their children have their own pages. Implemented
as `includeChildren` gated on `HasChildren` in the tile, with the
fan-out resolver in `AuditController.ResolveSubtreePairsAsync`.

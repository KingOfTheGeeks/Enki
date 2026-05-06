# Enki Architecture Decisions

*Last audited: 2026-05-06 against `main` HEAD `c3b589a`. 12 decisions current; #12 (Migrator three-channel output) added 2026-05-06.*

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
- Controllers stay deliberately thin — even the largest
  (`RunsController` ~800 lines, `SurveysController` ~750) are mostly
  XML doc + DTO mapping; the per-action method bodies are typically
  20-40 lines of straight EF + ProblemDetails plumbing. There's no
  business logic hiding in them.
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

The same column-source-of-truth pattern holds for the parallel
`UserType` (Team / Tenant) and `TeamSubtype` (Field / Office /
Supervisor) columns added in the authorization redesign — both are
columns on `ApplicationUser`, projected as `user_type` and
`team_subtype` claims at sign-in by the same factory.

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

## 6. Twelve named policies built from one parametric requirement.

**Status:** Adopted in commit `01206c2` (replaces the original
"single `EnkiAdmin` policy" decision below). See
[`docs/sop-authorization-redesign.md`](sop-authorization-redesign.md)
for the full matrix.

**Decision:** Every WebApi authorization gate references one of
thirteen named constants in `SDI.Enki.Shared.Authorization.EnkiPolicies`.
Twelve are constructed from a single parametric `TeamAuthRequirement`
(a record carrying optional `MinimumSubtype` / `GrantingCapability` /
`TenantScoped` / `RequireAdmin` flags) evaluated by one handler with
an 8-step decision tree; the thirteenth, `EnkiApiScope`, is the
default scope-only fallback. The policy *names* are stable; the
*predicate* is data-driven via the requirement's parameters.

Three classifications stack to determine audience: `UserType`
(Team / Tenant, immutable), `TeamSubtype` (Field / Office /
Supervisor; Team-only), and capability claims (additive grants —
currently just `Licensing`). `IsEnkiAdmin` short-circuits all
predicates.

**Rejected — Option A (per-endpoint hand-rolled handlers):** one
`IAuthorizationHandler` class per gate, each with its own
`HandleRequirementAsync`. The previous shape: `CanAccessTenantHandler`,
`CanManageTenantMembersHandler`, `EnkiAdminOnlyHandler`. Three
handlers, three near-duplicate decision trees, three test fixtures.

**Rejected — Option B (fine-grained admin scopes):** the original
v1 decision: keep one `EnkiAdmin` policy and treat tenant admins
as a separate per-membership concept. Concrete failure: the
per-membership `TenantUser.Role` (Admin / Contributor / Viewer)
never carried operational meaning — every policy that consulted it
flattened back to admin-or-not. The redesign drops the per-tenant
role column entirely (folded into the consolidated `Initial`
master-DB migration during the pre-customer schema squash) in
favour of the system-wide `TeamSubtype` hierarchy.

**Why parametric:**
- Adding a new gate (e.g. "Office can sync master Tools") is one
  policy registration in `Program.cs`, not a new handler class.
- The 8-step decision tree lives in *one* place. A single audit
  pass over `TeamAuthHandler` covers every policy in the system.
- Adding a new capability (e.g. `bulk-import`) is one constant
  added to `EnkiCapabilities.All` and one policy registration. No
  handler changes; the AdminUserDetail UI auto-renders a checkbox.
- The BlazorServer host registers parallel claim-assertion policies
  under the same names so `[Authorize(Policy = EnkiPolicies.CanFoo)]`
  works in Blazor pages too — a renamed policy fails to compile in
  both hosts.

**Trade:** the parametric requirement is more abstract than the
per-handler shape. The 8-step decision tree is one large method;
debugging a denial means stepping through it rather than reading a
2-line custom handler. The 21-test matrix in
`TeamAuthHandlerTests.cs` covers every cell of the tree, so changes
to the tree fail loudly.

`CanDeleteTenantContent` and `CanDeleteMasterContent` are kept as
distinct policy *names* even though they share the same predicate
as their `Write` siblings today — a future "delete needs Supervisor"
tightening lands as a one-line policy registration change with no
controller churn.

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

---

## 12. Migrator output is three-channel: `Console.WriteLine`, `Console.Error.WriteLine`, Serilog.

**Status:** Adopted.

**Decision:** Inside `SDI.Enki.Migrator`, command output is split across
three deliberately-distinct channels:

- `Console.WriteLine` — **end-of-command success summary only.** Clean
  key/value blocks the operator visually scans. No timestamp, no log
  level, no enrichers. Canonical shape:
  [`ProvisionCommand.RunAsync`](../src/SDI.Enki.Migrator/Commands/ProvisionCommand.cs)
  (the lines after `await svc.ProvisionAsync(...)` succeeds).
- `Console.Error.WriteLine` — **operator-facing errors at exit.** Used
  both for pre-host failures where Serilog isn't built yet
  ([`Program.cs`](../src/SDI.Enki.Migrator/Program.cs) — missing
  connection string, missing required secret) and for caught exceptions
  inside command bodies. Always paired with a non-zero exit code.
- `ILogger` (Serilog) — **structured detail during execution.**
  Step-by-step progress, retry attempts, full exception traces. Routes
  to both stdout (interactive console template) and the daily rolling
  file sink under `logs/enki-migrator-*.log`. See
  [`BootstrapEnvironmentCommand`](../src/SDI.Enki.Migrator/Commands/BootstrapEnvironmentCommand.cs)
  for the per-step pattern.

**Rejected:** unify everything through Serilog with a custom template
that drops the timestamp/level prefix for "operator messages."

**Why three-channel:**

- The audiences are different. An operator running
  `Enki.Migrator provision --code ...` wants a clean post-command
  summary; ops investigating a failed deploy wants the structured log
  file. One channel can't serve both without burying the success block
  under timestamps or polluting the file logs with non-structured
  ceremony text.
- `Console.Error` is the right place for errors regardless of Serilog
  state. Pre-host failures literally have no logger yet; errors caught
  later still need a one-line stderr message so operator UX stays
  consistent across every failure mode. Serilog's stdout-vs-stderr
  policy is a sink concern — using `Console.Error` directly makes the
  stream explicit at the call site.
- Both Serilog and `Console.WriteLine` land on stdout in interactive
  use, but they're stylistically distinct (Serilog has the
  `HH:mm:ss [INF]` prefix; `Console.WriteLine` doesn't), so the
  operator tells streaming progress apart from the final summary at a
  glance.

**Where this hurts:** a contributor reading the code for the first
time may see a `Console.WriteLine` and think "shouldn't this be
`logger.LogInformation`?" — and a hasty refactor of the success block
into Serilog would clutter the file sink with
`Tenant provisioned: PERMIAN` lines that have no investigation value.
The convention only sticks because of this entry.

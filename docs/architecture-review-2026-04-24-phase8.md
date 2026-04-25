# Enki Architecture Review — Phase 8 Follow-up

**Date:** 2026-04-24
**Branch:** main (HEAD `f1310b8` — Phase 8e)
**Scope:** Follow-up to `architecture-review-2026-04-24.md` (done at `6f88d86`, Phase 7c). Covers commits landed since:
- `b2e76fb` Phase 7d-1 — close cross-tenant auth gap + remove leaked dev creds
- `d1dbce5` Phase 7d-2 — robustness + consistency sweep
- `34da6c5` Phase 8a — admin shell
- `71de8e1` Phase 8b — user admin (Identity bearer endpoints + Blazor pages)
- `d780642` Phase 8c — tenant memberships + IdentityId-resolution bug fix
- `8f5bb56` Phase 8d — user preferences (self-service `/account/settings`)
- `f1310b8` Phase 8e — system settings (admin UI + first real setting)

This report is a hand-off to an implementation agent. Each finding is self-contained: it cites file:line, states the problem, explains why, and gives a concrete fix. Status of the prior review's 20 findings is tabulated at the top; new findings follow; systemic patterns come last.

---

## 1. Status of prior-review findings (20 items)

Current state against the earlier report. `FIXED` = fully addressed, `PARTIAL` = partly addressed, `OPEN` = still present, `OBSOLETE` = code restructured so finding no longer applies.

| #  | Prior title                                                                 | Status   | Evidence / note                                                                                                                                                                                 |
|----|-----------------------------------------------------------------------------|----------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 1  | Cross-tenant authorization gap on 5 controllers                             | FIXED    | All five now carry `[Authorize(Policy = EnkiPolicies.CanAccessTenant)]`.                                                                                                                        |
| 2  | No regression test for tenant-membership enforcement                        | FIXED    | `tests/SDI.Enki.WebApi.Tests/Integration/TenantAuthorizationTests.cs` exists with a full matrix.                                                                                                |
| 3  | `IdentitySeedData.SeedAsync` runs in every environment with hardcoded creds | PARTIAL  | Credentials now come from `IConfiguration` with a `Development`-only fallback (good). **But the `SeedAsync` call at `src/SDI.Enki.Identity/Program.cs:188` is still outside the `IsDevelopment()` gate that ends at line 185.** See finding 1 below. |
| 4  | Hardcoded `sa` connection string in design-time factories                   | FIXED    | Factories call `ConnectionStrings.RequireMaster()` which throws if `EnkiMasterCs` is unset.                                                                                                     |
| 5  | `SurveysController.Calculate` no length guard on parallel-index loop        | FIXED    | Guard added before the loop.                                                                                                                                                                    |
| 6  | `UnitSystem.Custom` accepted at API layer but unmapped in presets           | FIXED    | `JobsController.TryParseUnitSystem` filters `UnitSystem.Custom`.                                                                                                                                |
| 7  | Inconsistent error-response shapes across controllers                       | FIXED    | Controllers now use `EnkiResults.NotFoundProblem / ValidationProblem / ConflictProblem` uniformly.                                                                                              |
| 8  | `CreateWellDto` lacks validation attributes                                 | FIXED    | `[Required]` + `[MaxLength(200)]` applied.                                                                                                                                                      |
| 9  | `TenantRoutingMiddleware` cache not busted on deactivation                  | FIXED    | `TenantsController.Deactivate / Reactivate` call `cache.Remove(TenantRoutingMiddleware.CacheKeyFor(code))`.                                                                                     |
| 10 | `FindOrCreateAsync` race not handled                                        | OPEN     | `TenantDbContextLookupExtensions.FindOrCreateAsync` still lacks the unique-violation catch + re-query.                                                                                          |
| 11 | Best-effort exception swallowing without logging                            | FIXED    | `TenantProvisioningService` + `MigrateCommand` log via `LogWarning` on cleanup failures.                                                                                                        |
| 12 | N+1 subquery in `GradientsController.List`                                  | PARTIAL  | `g.Shots.Count` is still used inline at `GradientsController.cs:44` and `:61`. EF 10 may flatten this; nobody has measured. Turn on SQL logging once and confirm, then close or fix.            |
| 13 | `JobLifecycle` coverage not contract-tested                                 | FIXED    | `tests/SDI.Enki.Core.Tests/Lifecycle/JobLifecycleTests.cs` has the "every status has a row" contract test plus per-transition theories.                                                         |
| 14 | Dangling cross-tenant `ReferencedJob` not validated                         | FIXED    | Entity now has a class-level comment documenting the data-integrity contract (option 3 of the original fix).                                                                                    |
| 15 | Anaemic domain model with public setters                                    | OPEN     | Entities still expose property setters. Tracked as tech debt in the prior report.                                                                                                               |
| 16 | Auth-constant drift between `Identity` and `WebApi`                         | FIXED    | `src/SDI.Enki.Shared/Identity/AuthConstants.cs` is canonical; both hosts reference it.                                                                                                          |
| 17 | `NavMenu` couples to URL string layout                                      | OPEN     | `NavMenu.razor` still parses `NavigationManager.Uri` directly. No `ITenantScopeService`.                                                                                                        |
| 18 | `ApplicationUser.UserType` is a free-string                                 | OPEN     | Unchanged.                                                                                                                                                                                      |
| 19 | README is 7 bytes; `reset-dev.ps1` undocumented                             | PARTIAL  | `README.md` has a title now. No setup / tooling / config content as specified.                                                                                                                  |
| 20 | `AthenaMasterDbContext` retains legacy "Athena" name                        | OPEN     | Unchanged.                                                                                                                                                                                      |

**Net:** 11 fixed, 3 partial, 5 open, 1 tolerated-and-documented (#14). The 7d-1 and 7d-2 commits did the heavy lifting; the four blockers and most of the high/medium items from the prior review are gone.

---

## 2. New findings (Phase 8 code)

### Index

| # | Severity | Title |
|---|---|---|
| 1  | High    | `IdentitySeedData.SeedAsync` still runs unconditionally in non-Dev environments |
| 2  | High    | `SystemSettingsController` uses a dual gate (policy + inline role check) |
| 3  | High    | `IsEnkiAdmin` column and `role` claim are two sources of truth for the same fact |
| 4  | Medium  | `MeController` `/me/preferences` PUT returns bare JSON error, not ProblemDetails |
| 5  | Medium  | `AdminUsersController.List` has no paging |
| 6  | Medium  | `CanManageTenantMembersHandler` is ~80% a copy of `CanAccessTenantHandler` |
| 7  | Low     | LIKE-wildcard injection on `MasterUsersController.List` search |
| 8  | Low     | `[Required]` on a non-nullable `bool` in `SetAdminRoleDto` is a no-op |
| 9  | Low     | Temporary-password generator shrinks/biases the alphabet and appends a constant suffix |
| 10 | Low     | `AdminUsers` list `DisplayName` is the username; detail endpoint resolves a real name |

---

### 1. (High) `IdentitySeedData.SeedAsync` still runs unconditionally in non-Dev environments

**File:** `src/SDI.Enki.Identity/Program.cs:162-189`

**Problem:** The `using (var scope = app.Services.CreateAsyncScope())` block holds both the migration auto-apply (which is correctly gated on `IsDevelopment()` at line 164) and the seed call at line 188. Line 188 sits outside the `if` that ends at line 185, so `IdentitySeedData.SeedAsync(scope.ServiceProvider)` runs in every environment.

Phase 7d-1 hardened the creds themselves — in Production, a missing `Identity:Seed:DefaultUserPassword` / `Identity:Seed:BlazorClientSecret` now throws rather than silently using dev values. That drops the severity from Blocker to High. The residual risk: any operator who sets those keys in a Production config (for convenience during a cutover, for example) gets a seed re-run on every boot that could resurrect users who were deliberately removed and reset any client-metadata the seeder touches.

**Fix:** Move the call inside the existing gate:

```csharp
if (app.Environment.IsDevelopment())
{
    // ... existing migrate block ...
    await IdentitySeedData.SeedAsync(scope.ServiceProvider);
}
```

If Production ever needs a first-run seed, give it an explicit opt-in — e.g. `Identity:Seed:RunOnStartup` — with an `environment-gated-by-default` shape and loud logging when it fires.

**Verification:** Boot the Identity host with `ASPNETCORE_ENVIRONMENT=Production` and the two `Identity:Seed:*` keys unset. Today the host starts and the seed throws mid-flight; after the fix, the seed call is skipped entirely.

---

### 2. (High) `SystemSettingsController` uses a dual gate (policy + inline role check)

**File:** `src/SDI.Enki.WebApi/Controllers/SystemSettingsController.cs:25-78`

**Problem:** Both actions declare `[Authorize(Policy = EnkiPolicies.EnkiApiScope)]` at the method level, then call `if (!IsEnkiAdmin()) return Forbid();` as the first business-logic line. This is the exact shape of the 7d-1 cross-tenant gap — the policy admits a broader caller than the action actually serves, and the real boundary lives in a hand-rolled check one line below. A future author adding a new action to this controller has nothing stopping them from forgetting the inline check.

**Fix:** Register a dedicated policy alongside `CanAccessTenant` / `CanManageTenantMembers` in `Program.cs`:

```csharp
options.AddPolicy(EnkiPolicies.EnkiAdminOnly, p =>
{
    p.RequireAuthenticatedUser();
    p.RequireClaim(OpenIddictConstants.Claims.Private.Scope,
        AuthConstants.WebApiScope);
    p.RequireRole(AuthConstants.EnkiAdminRole);
});
```

Add the constant to `EnkiPolicies`. Move the `[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]` to the controller level. Delete `IsEnkiAdmin()` and the four `if (!IsEnkiAdmin()) return Forbid();` lines. Mirror the same policy name in the Identity host's existing `"EnkiAdmin"` registration so the two sides don't drift.

**Verification:** Call `GET /admin/settings` with a token that has `scope=enki` but no `enki-admin` role — must 403 before the handler runs.

---

### 3. (High) `IsEnkiAdmin` column and `role` claim are two sources of truth for the same fact

**Files:**
- `src/SDI.Enki.Identity/Data/ApplicationUser.cs` — `public bool IsEnkiAdmin { get; set; }` column
- `src/SDI.Enki.Identity/Data/IdentitySeedData.cs:~90-120` — seeder adds the column AND a `Claim("role", "enki-admin")`
- `src/SDI.Enki.Identity/Controllers/AdminUsersController.cs:120-148` — `SetAdminRole` updates the column, reconciles the claim, rotates the security stamp

**Problem:** The same fact ("this user is an Enki admin") is persisted in two places. Keeping them consistent relies on every writer remembering the three-step dance. The seeder does it, `SetAdminRole` does it — but a direct DB fix, a migration, or a future endpoint that only touches one side will desync them. Readers already pick different sides: `AdminUsersController.List` projects `u.IsEnkiAdmin` (column), `CanAccessTenantHandler` checks `User.IsInRole(...)` (claim). A desync ships different authorization answers to different call paths.

**Fix:** Pick one source of truth.

- **Option A — claims-only (conventional with Identity):** delete the `IsEnkiAdmin` column + the column reconcile in `SetAdminRole`. Use an `IdentityRole("enki-admin")` + `UserRoles` rather than a freeform claim (claims and roles are both fine; roles are idiomatic). The list endpoint projects `userMgr.GetUsersInRoleAsync("enki-admin")` → set lookup → boolean.
- **Option B — column authoritative:** keep the column, remove the claim-persistence. Add `IsEnkiAdmin` to the JWT via a `IUserClaimsPrincipalFactory<ApplicationUser>` override that emits the role claim at sign-in from the column. Readers continue to use `User.IsInRole`; the claim is derived, not stored.

Either way, there is one write path and no reconcile dance.

**Verification:** Flip `IsEnkiAdmin` via the chosen path, issue a fresh token, hit a tenant endpoint — the admin bypass should work. Then flip back and confirm it stops working after token refresh.

---

### 4. (Medium) `MeController` `/me/preferences` PUT returns bare JSON error, not ProblemDetails

**File:** `src/SDI.Enki.Identity/Controllers/MeController.cs:53, 60`

**Problem:** 7d-2 standardized the WebApi on RFC 7807 ProblemDetails via `EnkiResults`. The Identity host's new `/me/preferences` endpoint didn't get the memo: `return BadRequest(new { error = "..." })` and `return BadRequest(new { errors = result.Errors... })`. The Blazor client already carries a generic ProblemDetails error renderer; bare JSON falls through to the catch-all and gets rendered as "Unexpected error: …".

**Fix:** Either (a) make `EnkiResults` visible to the Identity host by moving its extension methods to `SDI.Enki.Shared` (they don't depend on anything WebApi-specific except the ControllerBase extension surface, which lives in Mvc.Core), or (b) add a small Identity-side equivalent — a file called `IdentityResults.cs` next to `MeController` with the same `ValidationProblem(string[] errors)` shape.

Given there are two Identity controllers today (`MeController`, `AdminUsersController`) and both have spots that currently return bare JSON, the shared path in `Shared` is the right choice.

**Verification:** `PUT /me/preferences` with `{"preferredUnitSystem":"Bogus"}` must return a 400 with `application/problem+json` body matching the WebApi shape.

---

### 5. (Medium) `AdminUsersController.List` has no paging

**File:** `src/SDI.Enki.Identity/Controllers/AdminUsersController.cs:41-68`

**Problem:** `List()` materializes every row in `AspNetUsers` into memory with `.ToListAsync()`. The Blazor grid is client-side (Syncfusion `SfGrid` with `Height="auto"` at `AdminUsers.razor:48`), so the whole set crosses the wire on every visit. Small today; an Enki deployment with 1000+ users will time out the Blazor render.

**Fix:** Add `[FromQuery] int skip = 0, int take = 100` parameters. `Skip / Take` before `ToListAsync`. Return a paginated envelope (`{ items, total }`). Wire the Syncfusion grid's virtual-scrolling or server-paging mode.

Also consider that the shape of this endpoint is going to be reused for similar admin grids (tenants, settings history, audit). A `PagedResult<T>` record in `Shared` used uniformly across admin endpoints is worth setting up now, not on the third controller.

**Verification:** Seed 1000 users; confirm the endpoint returns the first 100 in < 200 ms and the grid renders pagination controls.

---

### 6. (Medium) `CanManageTenantMembersHandler` is ~80% a copy of `CanAccessTenantHandler`

**Files:**
- `src/SDI.Enki.WebApi/Authorization/CanAccessTenantRequirement.cs:32-91`
- `src/SDI.Enki.WebApi/Authorization/CanManageTenantMembersRequirement.cs:19-72`

**Problem:** Both handlers do the same five things in the same order:
1. Read `sub` from the principal; bail if missing.
2. Allow cross-tenant `enki-admin` role to bypass.
3. Read `tenantCode` from route values; bail if missing.
4. Parse `sub` as `Guid identityId`; bail if malformed.
5. Query master DB for a `TenantUser` matching `(identityId, tenantCode)` — differing only in an extra `Role == Admin` clause.

Two handlers today; the next tightening (e.g. "CanManageJobs" — tenant Admin + Contributor) will be the third copy. Changes to logging, bypass rules, or claim names have to be made everywhere.

**Fix:** Extract a shared base or helper. Minimum viable change: protected method on a new abstract base class `TenantScopedAuthorizationHandler<TRequirement>` that exposes a `Task<AuthContext?> ResolveAsync(AuthorizationHandlerContext ctx)` returning `(Guid IdentityId, string TenantCode)` or `null` if the precondition fails. Concrete handlers implement only the final predicate.

Alternative: keep the handlers as-is but extract a static `AuthContextExtractor` with a single method that returns the tuple (or null on failure) and does the logging. Zero inheritance, still dedupes the parsing.

**Verification:** Unit test (`tests/SDI.Enki.WebApi.Tests/Authorization/`) that both handlers reject the same set of bad inputs (no sub, malformed sub GUID, no tenantCode).

---

### 7. (Low) LIKE-wildcard injection on `MasterUsersController.List` search

**File:** `src/SDI.Enki.WebApi/Controllers/MasterUsersController.cs:38`

**Problem:** `query.Where(u => EF.Functions.Like(u.Name, $"%{trimmed}%"))`. The user input is parameterized (this is not SQL injection), but `%`, `_`, `[`, `]` in `trimmed` act as wildcards. An admin typing `100%` matches everything; typing `_mith` matches `Smith`, `Jsmith`, `ksmith`. Harmless for this endpoint (it only powers the Add-member picker), but the pattern will get copy-pasted to less-admin endpoints.

**Fix:** Either:

```csharp
query = query.Where(u => u.Name.Contains(trimmed));
```

EF translates `.Contains` to `LIKE` with an `ESCAPE` clause automatically. Or if you want to keep `EF.Functions.Like`:

```csharp
var escaped = trimmed
    .Replace("!", "!!")
    .Replace("%", "!%")
    .Replace("_", "!_")
    .Replace("[", "![");
query = query.Where(u => EF.Functions.Like(u.Name, $"%{escaped}%", "!"));
```

Prefer `Contains`.

**Verification:** Search for `%` — result set should match only users with a literal `%` in their name (empty in dev), not every user.

---

### 8. (Low) `[Required]` on a non-nullable `bool` in `SetAdminRoleDto` is a no-op

**File:** `src/SDI.Enki.Shared/Identity/AdminUserDtos.cs:43-44`

**Problem:** `public sealed record SetAdminRoleDto([Required] bool IsAdmin);`. `[Required]` fires on `null`; a non-nullable `bool` can never be null, so the attribute never fires. An empty POST body deserializes to `IsAdmin = false` and gets through. If "must be explicitly provided" is the intent, the property should be `bool?`.

**Fix:** Either:

```csharp
public sealed record SetAdminRoleDto([Required] bool? IsAdmin);
```

and validate `.IsAdmin.HasValue` in the controller, or drop the attribute if the `false`-on-missing default is acceptable.

**Verification:** `POST /admin/users/{id}/admin` with `{}` — today the request succeeds and sets `IsAdmin=false`. Decide whether that's what you want.

---

### 9. (Low) Temporary-password generator shrinks/biases the alphabet and appends a constant suffix

**File:** `src/SDI.Enki.Identity/Controllers/AdminUsersController.cs:185-195`

**Problem:**

```csharp
var bytes = RandomNumberGenerator.GetBytes(12);
var alphaNum = Convert.ToBase64String(bytes)
    .TrimEnd('=')
    .Replace('+', 'A')
    .Replace('/', 'b');
return alphaNum + "!9Ax";
```

Three smells:
- `Replace('+','A')` and `Replace('/','b')` shrink the alphabet to 62 chars while double-counting `A` and `b`, so entropy is slightly biased.
- Entropy is still ~96 bits so the password is not weak in practice.
- Every generated password ends in the literal `!9Ax`, which is a visible signature that gives an attacker the tail four chars for free (drops effective entropy by 24 bits if an attacker sees one example).

**Fix:** Use a purpose-built alphabet:

```csharp
private const string Alphabet =
    "ABCDEFGHJKLMNPQRSTUVWXYZ" +
    "abcdefghjkmnpqrstuvwxyz" +
    "23456789" +
    "!@#$%^&*";

private static string GenerateTemporaryPassword()
{
    Span<char> chars = stackalloc char[16];
    for (int i = 0; i < chars.Length; i++)
        chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
    return new string(chars);
}
```

Guarantees one of each character class isn't strictly required (the user resets it immediately), but if you want to keep the policy-satisfying shape, bucket the first 4 slots by class and fill the rest uniformly.

**Verification:** Generate 10k passwords; confirm no two share a tail, and the class distribution passes the Identity password policy.

---

### 10. (Low) `AdminUsers` list `DisplayName` is the username; detail endpoint resolves a real name

**File:** `src/SDI.Enki.Identity/Controllers/AdminUsersController.cs:65` vs `:79`

**Problem:** `List` sets `DisplayName = u.UserName ?? ""`, the comment at :65 explaining "detail endpoint resolves the friendly name". `Get` loads claims and pulls the `name` claim. Users who have a friendly name see "jdoe" in the list and "John Doe" on the detail — jarring, and the `DisplayName` field name implies they match.

**Fix:** One of:
- Load claims in the list query (`userMgr.Users.Include(u => u.UserClaims).Where(...)` if the Identity schema exposes the nav; otherwise a join). Add a computed `DisplayName` column on `ApplicationUser` populated when the `name` claim is written.
- Drop `DisplayName` from `AdminUserSummaryDto` and rename it for clarity if it comes back later — calling it `DisplayName` when it's really the username is a small lie.

**Verification:** A seeded user with a `name` claim of "John Doe" — list shows "John Doe", detail shows "John Doe", both identical.

---

## 3. Systemic patterns

Cross-cutting observations that don't fit a single file.

### S1. `TryParse<SmartEnum>` duplicated in four controllers (plus an inline allowlist)

**Files:**
- `src/SDI.Enki.WebApi/Controllers/JobsController.cs:182-190`
- `src/SDI.Enki.WebApi/Controllers/RunsController.cs:103-110`
- `src/SDI.Enki.WebApi/Controllers/WellsController.cs:78-85`
- `src/SDI.Enki.WebApi/Controllers/TenantMembersController.cs:135-142`
- `src/SDI.Enki.Identity/Controllers/MeController.cs:79-80` — inline `s is "Field" or "Metric" or "SI"` (deliberate, to avoid the Core project ref)

Every instance has the same shape: a false branch that sets `role = null!` as a nullability suppression. Lying to the compiler is habit-forming.

**Fix:** One extension, in `SDI.Enki.Core.Abstractions` next to the SmartEnum imports:

```csharp
public static bool TryFromName<T>(string? name, [NotNullWhen(true)] out T? value)
    where T : SmartEnum<T>
{
    if (string.IsNullOrWhiteSpace(name))
    {
        value = null;
        return false;
    }
    value = SmartEnum<T>.List
        .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
    return value is not null;
}
```

Delete the five copies. `MeController` takes the Core ref (or keeps the inline allowlist and accepts drift). Either is fine — just decide once.

---

### S2. Error-message enum lists hardcoded as English prose

**Files:** same as S1, plus repetitions inside the same controller.

Each `TryParse` failure path writes a literal like `"Expected Gradient, Rotary, or Passive."`. Enum names duplicated into six or seven strings across the code base. The new values `TenantUserRole.Admin / Contributor / Viewer` (added in Phase 8c) had to be typed into two `ValidationProblem` messages; the next role added will be a silent drift for a while.

**Fix:** Generate the allowlist at the point of failure:

```csharp
var expected = string.Join(", ", SmartEnum<T>.List.Select(x => x.Name));
var message = $"Unknown {typeof(T).Name} '{name}'. Expected {expected}.";
```

Combine with S1: the new `TryFromName<T>` helper can return a generated error message via an out-param or a result tuple.

---

### S3. Nullability suppression as a local habit

Scan `src/` for `= null!` and audit each occurrence. EF-mapped required-reference properties legitimately use it; `TryParse` false branches do not. A handful of the latter encourages readers to treat `= null!` as "trust me" rather than "this is a real EF contract". The S1 refactor removes five of them at once.

---

### S4. `HttpContextAccessor` injection in authorization handlers for route values

**Files:**
- `src/SDI.Enki.WebApi/Authorization/CanAccessTenantRequirement.cs:32-63`
- `src/SDI.Enki.WebApi/Authorization/CanManageTenantMembersRequirement.cs:19-44`

Both inject `IHttpContextAccessor` to reach `Request.RouteValues["tenantCode"]`. Under ASP.NET Core endpoint routing the `AuthorizationHandlerContext.Resource` is set to the `HttpContext` — no accessor needed. Not a bug; an idiom drift that (a) adds a DI dependency the handlers don't actually need and (b) makes them slightly harder to unit-test (`HttpContextAccessor` is stateful; `Resource` is just a parameter).

**Fix:** Change the injection and use:

```csharp
protected override async Task HandleRequirementAsync(
    AuthorizationHandlerContext context, ...)
{
    var http = context.Resource as HttpContext ?? throw new InvalidOperationException(...);
    var tenantCode = http.Request.RouteValues["tenantCode"] as string;
    ...
}
```

Low priority; do it next time these handlers are opened.

---

### S5. Entity → DTO projection inlined per endpoint

Every list/detail endpoint has an inline `Select(e => new XxxDto(...))`. List and detail DTOs for the same entity duplicate the core of the projection with different tails. This is fine at two endpoints, starts to drift at five. It also hides the N+1 concern in finding #12 of the prior review — the `g.Shots.Count` expression is repeated in two different controllers because there's no shared projection function.

**Fix (if/when it starts to hurt):** a `Projections/` folder per domain area with `IQueryable<Run> ToSummaryDto()` / `ToDetailDto()` extensions. No AutoMapper — typed expression trees, one source of truth for the shape.

Not worth doing today. File the idea; revisit at the third duplicate of the same entity.

---

### S6. Clean-architecture boundary check

Sanity confirmation, not an action item:
- `Core` references only `Ardalis.GuardClauses` + `Ardalis.SmartEnum` + BCL. No EF Core, no ASP.NET. ✓
- `Shared` is a leaf (DTOs + constants). ✓
- `Infrastructure` owns DbContexts and factories; WebApi consumes via `ITenantDbContextFactory` + `AthenaMasterDbContext` direct injection.
- The one deliberate deviation: **controllers talk to `DbContext` directly**, not through repositories/specs/handlers. Given the prior reviewer's note that MediatR + Specification were dropped, this is a deliberate simplification, and it's working — but it means the "let's introduce CQRS" refactor is non-trivial (it would rewrite every controller). Document the choice in `README.md` or a `docs/ArchDecisions.md` so the next engineer doesn't spend a week proposing a refactor that's already been considered and rejected.

---

## 4. Claims verified and rejected

For honesty: three exploration-pass claims were wrong on inspection and have been dropped rather than written up.

- **"`RunsController.Get` has `AsNoTracking` after `Include`."** The code at `RunsController.cs:51-54` is `.AsNoTracking().Include(r => r.Operators).FirstOrDefaultAsync(...)`. That is the correct order.
- **"`SystemSettingKeys.All` is mutable."** The property at `SystemSetting.cs:59-62` is `public static readonly IReadOnlyList<string>` backed by a collection expression `[ JobRegionSuggestions ]`. The compiler emits an immutable array. Nothing to fix.
- **"`TenantMembersController.Remove` silently succeeds on already-deleted rows."** The code at `TenantMembersController.cs:122-128` calls `FirstOrDefaultAsync` and returns `NotFoundProblem("Membership", ...)` before the delete. It is explicit, not silent.

---

## 5. Suggested execution order

Smallest fix, biggest payoff first:

1. **PR 1 — Polish pass (~1 day):** findings 1, 7, 8, 9 + delete the `IdentitySeedData.WebApiScope` / `EnkiAdminRole` re-exports (the class comment admits no caller needs them anymore).
2. **PR 2 — Auth consolidation (~0.5 day):** finding 2 (new `EnkiAdminOnly` policy) + finding 6 (handler dedup). Both touch auth; ship together with the corresponding tests.
3. **PR 3 — Consistency sweep (~1 day):** finding 4 (`MeController` ProblemDetails) + S1 + S2 (generic `TryFromName<T>` + generated error messages). Biggest readability win, lowest risk.
4. **PR 4 — Scale prep (~0.5 day):** finding 5 (`AdminUsers` paging + a `PagedResult<T>` in `Shared`).
5. **PR 5 — Design-call first, then a PR:** finding 3 (`IsEnkiAdmin` column vs claim — pick one).
6. **Backlog:** everything still OPEN on the prior review (#10, #15, #17, #18, #20) plus S4 / S5 / S6-the-doc.

Each PR should keep `TreatWarningsAsErrors=true` green and add tests for the behavior it changes.

---

## 6. Verify before acting

This report was written against HEAD `f1310b8`. Before touching any cited file, run `git log -- <file>` to confirm it hasn't moved. Line numbers in particular drift fast on Phase 8's active files.

Two claims worth re-checking at implementation time:

- **Finding 3 assumes `IsEnkiAdmin` is a real column on `ApplicationUser`.** If a migration in flight removes it, the finding becomes moot — confirm with `git grep IsEnkiAdmin` before designing the dedup.
- **Prior-review #12 (N+1 in Gradients)** — turn on EF SQL logging once (`UseLoggerFactory(...)` + a console logger at `Information` level on the tenant DbContext) and grab the actual SQL for `GET /tenants/{code}/jobs/{id}/runs/{id}/gradients`. If it's a single statement with a `LEFT JOIN ... GROUP BY`, close the finding. If it's N subqueries, switch to the manual count-zip.

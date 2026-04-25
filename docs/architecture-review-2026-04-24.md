# Enki Architecture Review — Action Report

**Date:** 2026-04-24
**Branch:** main (HEAD `6f88d86` — Phase 7c)
**Scope:** Full review of `src/` and `tests/` — Core, Shared, Infrastructure, Identity, WebApi, BlazorServer, Migrator.

This report is a hand-off to an implementation agent. Each finding is self-contained: it cites file:line, states the problem, explains why, and gives a concrete fix. Work the **Blocker** items first — items 1–4 are exploitable security issues. Items in **Verify before acting** are claims that should be confirmed against current code in case anything has shifted.

---

## Index

| # | Severity | Title |
|---|---|---|
| 1 | **Blocker** | Cross-tenant authorization gap on 5 controllers |
| 2 | **Blocker** | No regression test for tenant-membership enforcement |
| 3 | **Blocker** | `IdentitySeedData.SeedAsync` runs in every environment with hardcoded dev password + dev client secret |
| 4 | **High** | Hardcoded `sa` connection string in design-time factories |
| 5 | **High** | `SurveysController.Calculate` has no length guard on parallel-index loop |
| 6 | **High** | `UnitSystem.Custom` accepted at API layer but unmapped in presets |
| 7 | **Medium** | Inconsistent error-response shapes across controllers |
| 8 | **Medium** | `CreateWellDto` lacks validation attributes |
| 9 | **Medium** | `TenantRoutingMiddleware` cache not busted on deactivation |
| 10 | **Medium** | `FindOrCreateAsync` race not handled |
| 11 | **Medium** | Best-effort exception swallowing without logging |
| 12 | **Medium** | N+1 subquery in `GradientsController.List` |
| 13 | **Medium** | `JobLifecycle` coverage not contract-tested |
| 14 | **Low** | Dangling cross-tenant `ReferencedJob` not validated |
| 15 | **Low** | Anaemic domain model with public setters |
| 16 | **Low** | Auth-constant drift between `Identity` and `WebApi` |
| 17 | **Low** | `NavMenu` couples to URL string layout |
| 18 | **Low** | `ApplicationUser.UserType` is a free-string |
| 19 | **Low** | README is 7 bytes; `reset-dev.ps1` undocumented |
| 20 | **Low** | `AthenaMasterDbContext` retains legacy "Athena" name |

---

## 1. (Blocker) Cross-tenant authorization gap on 5 controllers

**Files:**
- `src/SDI.Enki.WebApi/Controllers/WellsController.cs:17`
- `src/SDI.Enki.WebApi/Controllers/RunsController.cs:17`
- `src/SDI.Enki.WebApi/Controllers/ShotsController.cs:23`
- `src/SDI.Enki.WebApi/Controllers/SurveysController.cs:22`
- `src/SDI.Enki.WebApi/Controllers/GradientsController.cs:17`

**Problem:** All five carry `[Microsoft.AspNetCore.Authorization.Authorize(Policy = "EnkiApiScope")]`. That policy only verifies the caller holds a token with `scope=enki` — it does **not** verify the caller is a member of the tenant whose data they're accessing. `JobsController.cs:43` correctly uses `EnkiPolicies.CanAccessTenant`, but the other five do not.

`TenantRoutingMiddleware` resolves `{tenantCode}` regardless of the caller's identity, so a logged-in user from tenant A can issue:

```
GET  /tenants/B/wells
GET  /tenants/B/jobs/{guid}/runs
GET  /tenants/B/jobs/{guid}/runs/{guid}/gradients
GET  /tenants/B/shots/{int}
POST /tenants/B/jobs/{guid}/wells/{int}/surveys/calculate
```

…and read or mutate tenant B's data. `ShotsController` is especially exposed because shot IDs are sequential ints — enumeration is trivial.

**Fix:**
1. On all five controllers, replace
   `[Microsoft.AspNetCore.Authorization.Authorize(Policy = "EnkiApiScope")]`
   with
   `[Authorize(Policy = EnkiPolicies.CanAccessTenant)]`
   (and add `using SDI.Enki.WebApi.Authorization;` + `using Microsoft.AspNetCore.Authorization;` as needed).
2. Verify no other controller uses the string literal `"EnkiApiScope"` — grep `"EnkiApiScope"` across `src/` should return only `EnkiPolicies.cs` and `Program.cs`. Existing matches in controllers should switch to `EnkiPolicies.EnkiApiScope`.

**Verification:** After the fix, write the test in finding #2 and run it — it should pass.

---

## 2. (Blocker) No regression test for tenant-membership enforcement

**File:** `tests/SDI.Enki.WebApi.Tests/Integration/` — no test currently asserts that a user not in tenant X is denied at `/tenants/X/...` routes. `TenantDataIsolationTests.cs` only checks that *data* doesn't bleed across DB connections — it stubs the auth pipeline and does not exercise `CanAccessTenantHandler`.

**Problem:** Finding #1 should have been caught by an automated test. There is no guard against it regressing once fixed.

**Fix:** Add `tests/SDI.Enki.WebApi.Tests/Integration/TenantAuthorizationTests.cs` covering:

1. **User in tenant A → 403 on `/tenants/B/...`** — for every tenant-scoped controller (jobs, wells, runs, shots, surveys, gradients).
2. **User in tenant A → 200 on `/tenants/A/...`** — sanity check that a member is admitted.
3. **User with `enki-admin` role → 200 on `/tenants/B/...`** — validates the admin bypass at `CanAccessTenantRequirement.cs:51`.
4. **Unauthenticated → 401** for any `/tenants/{code}/...` route.

Use the existing `EnkiTestWebApplicationFactory` and extend `TestAuthHandler` to issue principals with configurable `sub` and role claims. Assertions should check the HTTP status, not the body.

---

## 3. (Blocker) `IdentitySeedData.SeedAsync` runs in every environment with hardcoded dev password + dev client secret

**Files:**
- `src/SDI.Enki.Identity/Program.cs:172` — calls `IdentitySeedData.SeedAsync(scope.ServiceProvider);` *unconditionally*. The migration auto-apply just above it is gated on `IsDevelopment()`, but the seed call is outside that gate.
- `src/SDI.Enki.Identity/Data/IdentitySeedData.cs:78` — every seeded user is created with password `"Enki!dev1"`.
- `src/SDI.Enki.Identity/Data/IdentitySeedData.cs:172` — Blazor OIDC client registered with `ClientSecret = "enki-blazor-dev-secret"`.
- `src/SDI.Enki.Identity/Data/IdentitySeedData.cs:202-206` — the OpenIddict application is **upserted** every startup, so even if an admin rotates the secret in the OpenIddict tables it gets reset to the dev value on next reboot.

**Problem:** If the Identity host ever boots in a non-dev environment, the seeded users (including admins from `SeedUsers.All`) get the well-known dev password, and the Blazor client gets the well-known dev secret. The class-level comment at `IdentitySeedData.cs:32-35` says "rotate via the admin UI in any non-dev environment" — but no admin UI exists yet, and even if it did, the unconditional upsert at line 202–206 would undo the rotation on every restart.

**Fix:**
1. In `Program.cs`, gate the seed:
   ```csharp
   if (app.Environment.IsDevelopment())
       await IdentitySeedData.SeedAsync(scope.ServiceProvider);
   ```
   Move the `using` block accordingly. Confirm prod has another mechanism (an admin tool or a manual DB seed) for first-time setup.
2. In `IdentitySeedData.cs`, change the password and client secret to come from `IConfiguration` with **no fallback** — throw if missing. Suggested config keys: `Identity:Seed:DefaultUserPassword`, `Identity:Seed:BlazorClientSecret`. Pass `IConfiguration` through `SeedAsync(IServiceProvider sp)`.
3. Change the `clientMgr.UpdateAsync(existing, blazorDescriptor)` branch at line 206 to only update non-secret fields (redirect URIs, permissions). Never overwrite an existing client secret. Or skip the entire update if the client already exists.

**Verification:** After the fix, boot the Identity host with `ASPNETCORE_ENVIRONMENT=Production` and `Identity:Seed:DefaultUserPassword` unset — startup must fail loudly, not silently use a fallback.

---

## 4. (High) Hardcoded `sa` connection string in design-time factories

**Files:**
- `src/SDI.Enki.Infrastructure/DesignTime/TenantDbContextFactory.cs:23`
- `src/SDI.Enki.Infrastructure/DesignTime/AthenaMasterDbContextFactory.cs:23`

**Problem:** Both factories fall back to a hardcoded SQL connection string containing `User Id=sa;Password=!@m@nAdm1n1str@t0r` if the `EnkiMasterCs` environment variable is not set. This string is committed to the repo. It is intended for `dotnet ef` design-time tooling but appears in compiled binaries and any place the factory is reflected on. If the repo is ever made public (or shared with a contractor) the credentials leak.

**Fix:** Remove the fallback. If `EnkiMasterCs` is unset, throw `InvalidOperationException` with a clear message:
```csharp
var cs = Environment.GetEnvironmentVariable("EnkiMasterCs")
    ?? throw new InvalidOperationException(
        "Set EnkiMasterCs env var before running 'dotnet ef' against this project. " +
        "Example: $env:EnkiMasterCs = 'Server=...;Database=Enki_Master;...'");
```
After the fix, run `dotnet ef migrations list` against both projects with no env var set and confirm the message appears.

---

## 5. (High) `SurveysController.Calculate` has no length guard on parallel-index loop

**File:** `src/SDI.Enki.WebApi/Controllers/SurveysController.cs:75-89`

**Problem:** The handler reads survey rows from the DB, hands them to `ISurveyCalculator.Process`, then iterates `surveys` and the returned `MardukSurveyStation[]` by index, assuming equal length. If Marduk ever returns a different count (a bug, a partial result, an edge case where a tie-on row is dropped), the code either silently skips rows or throws `IndexOutOfRangeException`.

**Fix:** Before the loop, add:
```csharp
if (surveys.Count != computed.Length)
    throw new InvalidOperationException(
        $"Survey calculator returned {computed.Length} stations for {surveys.Count} input surveys.");
```
The `EnkiExceptionHandler` will map this to a 500 ProblemDetails. If a domain-specific exception type fits better (e.g. `EnkiException` subclass), use that.

---

## 6. (High) `UnitSystem.Custom` accepted at API layer but unmapped in presets

**Files:**
- `src/SDI.Enki.Core/Units/UnitSystem.cs:60-62` — defines `Custom` value.
- `src/SDI.Enki.Core/Units/UnitSystemPresets.cs` — `BuildMap()` does **not** include a `Custom` entry; `Get(UnitSystem.Custom, ...)` will throw.
- `src/SDI.Enki.WebApi/Controllers/JobsController.cs` — `TryParseUnitSystem` will accept the string `"Custom"` because `UnitSystem.FromName("Custom")` returns the SmartEnum value.

**Problem:** A Job persisted with `UnitSystem.Custom` will throw `InvalidOperationException("No unit preset for Custom / ...")` the first time any code calls `Measurement.As(UnitSystem.Custom)`. The class comment says custom presets are reserved for Phase 7e (sparse overrides). Until 7e ships, anyone who supplies `"Custom"` to `POST /tenants/{code}/jobs` lands a time bomb.

**Fix:** Reject `Custom` at the controller boundary. In `JobsController.TryParseUnitSystem` (or wherever the parse happens), after parsing successfully:
```csharp
if (parsed == UnitSystem.Custom)
    return false; // or return a ValidationProblem with a clear message
```
Mirror the same guard in `JobUpdate`. Add a unit test that `POST /tenants/.../jobs` with `unitSystem: "Custom"` returns 400.

---

## 7. (Medium) Inconsistent error-response shapes across controllers

**Files:**
- `src/SDI.Enki.WebApi/Controllers/RunsController.cs:27` — `NotFound(new { error = "..." })`
- `src/SDI.Enki.WebApi/Controllers/GradientsController.cs:28, 30` — `NotFound(new { error })`, `BadRequest(new { error })`
- `src/SDI.Enki.WebApi/Controllers/WellsController.cs` — similar pattern.
- Compare to `src/SDI.Enki.WebApi/Controllers/JobsController.cs` and `TenantsController.cs` which use `EnkiResults.NotFoundProblem(...)` etc. (RFC 7807 ProblemDetails).

**Problem:** The Blazor client and any external consumer get two different error JSON shapes depending on the route. Generic error rendering on the client requires a single contract.

**Fix:** Replace every `return NotFound(new { error = "..." })`, `BadRequest(new { error = "..." })`, `Conflict(new { error = "..." })` in controllers with the matching `EnkiResults` extension method (`NotFoundProblem`, `ValidationProblem`, `ConflictProblem`). Add `using SDI.Enki.WebApi.ExceptionHandling;` to the affected files. Pass entityKind/entityKey where the extension takes them so the response carries structured data.

---

## 8. (Medium) `CreateWellDto` lacks validation attributes

**File:** `src/SDI.Enki.Shared/Wells/CreateWellDto.cs`

**Problem:** Compare to `src/SDI.Enki.Shared/Tenants/ProvisionTenantDto.cs` and `src/SDI.Enki.Shared/Jobs/CreateJobDto.cs` — both use `[Required]`, `[MaxLength]`, `[RegularExpression]`. `CreateWellDto` has neither. Any string (including empty or oversized) lands in the DB. The `[ApiController]` auto-validation never fires because there's nothing to validate.

**Fix:** Add `[Required]` and `[MaxLength(N)]` matching the column constraints in `Well.cs`. If `Name` has a column max length of 200, write `[MaxLength(200)]`. Verify the column lengths from the EF model and use the same numbers.

---

## 9. (Medium) `TenantRoutingMiddleware` cache not busted on deactivation

**File:** `src/SDI.Enki.WebApi/Multitenancy/TenantRoutingMiddleware.cs:18, 33-34`

**Problem:** `IMemoryCache` holds resolved `TenantContext` objects for 5 minutes, keyed by `enki.tenant.{code}`. When `TenantsController.Deactivate` flips a tenant to `Status.Inactive`, requests already in the cache continue to serve the active connection string for up to 5 minutes. Acceptable for one-process dev; problematic at scale where the cache is per-instance and an attacker has up to 5 minutes after revocation to keep reading.

**Fix (small):** In `TenantsController.Deactivate` and `Reactivate`, inject `IMemoryCache` and call `cache.Remove($"enki.tenant.{code}")` on success.

**Fix (longer-term):** Make the duration configurable (`Multitenancy:TenantContextCacheSeconds`) and document the staleness window.

---

## 10. (Medium) `FindOrCreateAsync` race not handled

**File:** `src/SDI.Enki.Infrastructure/Data/Lookups/TenantDbContextLookupExtensions.cs:38-56`

**Problem:** Query → if-missing → insert with no transaction. Two concurrent shot creates with identical Magnetics will both miss the query, both attempt to insert, and the second hits the unique constraint and throws an unhandled `DbUpdateException`. The DB-level unique index keeps data consistent, but the loser request fails with a 500 instead of returning the winner's row.

**Fix:** Wrap the insert in a try/catch and recover on unique-violation:
```csharp
try
{
    db.Set<T>().Add(newEntity);
    await db.SaveChangesAsync(ct);
    return newEntity;
}
catch (DbUpdateException ex) when (IsUniqueViolation(ex))
{
    // Another request inserted the same row; re-query and return it.
    return await db.Set<T>().AsNoTracking().FirstAsync(matchExpression, ct);
}
```
`IsUniqueViolation` checks the inner `SqlException.Number` (2627 or 2601 on SQL Server). Add a unit test that runs two `FindOrCreateAsync` calls in parallel against an in-memory or test SQL Server and asserts both see the same row ID.

---

## 11. (Medium) Best-effort exception swallowing without logging

**Files:**
- `src/SDI.Enki.Infrastructure/Provisioning/TenantProvisioningService.cs:110-111`
- `src/SDI.Enki.Migrator/Commands/MigrateCommand.cs:142-143, 146-147`

**Problem:** `catch { /* best-effort */ }` blocks (cleanup of failed-status persistence, Archive→READ_ONLY re-flip on migration failure) silently drop exceptions. If cleanup fails, the database is left in an inconsistent state and there is zero signal in logs.

**Fix:** Replace each with a logged catch:
```csharp
catch (Exception ex)
{
    logger.LogWarning(ex,
        "Best-effort cleanup failed for tenant {TenantCode} after primary failure.",
        request.Code);
}
```
Inject `ILogger<TenantProvisioningService>` if not already present. The MigrateCommand already has access to a logger.

---

## 12. (Medium) N+1 subquery in `GradientsController.List`

**File:** `src/SDI.Enki.WebApi/Controllers/GradientsController.cs:36-38`

**Problem:** The `Select(g => new GradientSummaryDto(..., g.Shots.Count))` causes EF to emit a correlated subquery (`SELECT COUNT(*) FROM Shots WHERE GradientId = g.Id`) for every row. With 1000 gradients = 1000 subqueries inside one statement.

**Fix (preferred):** Use a `GroupJoin` or `Select` with a navigation that EF can flatten:
```csharp
var items = await db.Gradients
    .AsNoTracking()
    .Where(g => g.RunId == runId)
    .OrderBy(g => g.Order)
    .Select(g => new GradientSummaryDto(
        g.Id, g.Order, g.Description,
        g.Shots.Count(),  // EF 8+ translates this to a single LEFT JOIN + COUNT GROUP BY
        ...))
    .ToListAsync(ct);
```
Verify with EF query logging that the generated SQL is a single statement, not N+1. If EF still emits subqueries on this version, materialize counts in a separate query and zip in memory.

**Fix (defer):** If current data volumes are small (<100 gradients per run), add a TODO comment and revisit once load justifies it.

---

## 13. (Medium) `JobLifecycle` coverage not contract-tested

**File:** `tests/SDI.Enki.Core.Tests/` — no test exists.

**Problem:** `UnitSystemPresetsTests` has an excellent contract test that *every* preset covers *every* quantity — adding a new `EnkiQuantity` without a preset entry fails the test. There is no equivalent for `JobStatus` ↔ `JobLifecycle.AllowedTransitions`. Adding a new status without a transition map row is silently broken.

**Fix:** Add `tests/SDI.Enki.Core.Tests/Lifecycle/JobLifecycleTests.cs`:

```csharp
[Fact]
public void Every_JobStatus_has_an_AllowedTransitions_entry()
{
    foreach (var status in JobStatus.List)
    {
        Assert.True(
            JobLifecycle.AllowedTransitions.ContainsKey(status),
            $"JobStatus.{status.Name} has no row in JobLifecycle.AllowedTransitions");
    }
}

[Fact]
public void Self_transition_is_always_allowed()
{
    foreach (var status in JobStatus.List)
        Assert.True(JobLifecycle.CanTransition(status, status));
}

[Theory]
[InlineData(...)]  // each known transition pair
public void Known_transitions(JobStatus from, JobStatus to, bool allowed)
{
    Assert.Equal(allowed, JobLifecycle.CanTransition(from, to));
}
```

---

## 14. (Low) Dangling cross-tenant `ReferencedJob` not validated

**File:** `src/SDI.Enki.Core/TenantDb/Jobs/ReferencedJob.cs`

**Problem:** `ReferencedJobId` points at a job in another tenant's database. No FK (impossible cross-DB), no validator, no soft-delete propagation. If the referenced job is deleted, the reference dangles silently.

**Fix (one of):**
- Add a validator service that, on insert of a `ReferencedJob`, opens a context against the referenced tenant and confirms the job exists. Reject otherwise.
- Add a scheduled background task that scans `ReferencedJob` rows and marks dangling references.
- Document the data-integrity contract explicitly: "consumers must tolerate dangling references; the UI should fall back to a stub."

Pick one and ship the supporting code or comment. Today the entity comment notes "no SQL FK" but doesn't say what's compensating for it.

---

## 15. (Low) Anaemic domain model with public setters

**Files:** `src/SDI.Enki.Core/TenantDb/Jobs/Job.cs`, `src/SDI.Enki.Core/Master/Tenants/Tenant.cs`, `src/SDI.Enki.Core/TenantDb/Runs/Run.cs`, and most other entities.

**Problem:** Every property has a public setter. Business invariants (e.g. `EndTimestamp ≥ StartTimestamp`, `JobStatus` only changes through `JobLifecycle`) live in the controller layer. Today only `JobLifecycle` is enforced — and finding #1 shows what happens when one controller forgets.

**Fix (incremental):**
- Make audit fields (`CreatedAt`, `CreatedBy`, `RowVersion`) `init`-only with `private set` or use `init` accessors. The `SaveChangesAsync` interceptor uses property-level reflection so this should still work.
- Move state transitions onto the entity: `Job.TransitionTo(JobStatus next)` that consults `JobLifecycle.CanTransition` and throws if invalid. The controller becomes `job.TransitionTo(target); await db.SaveChangesAsync(ct);`.
- Leave anaemic-style getters/setters on properties that are genuinely just data (Name, Description).

This is a multi-week refactor — file as a tracked tech-debt item, not a single PR.

---

## 16. (Low) Auth-constant drift between `Identity` and `WebApi`

**Files:**
- `src/SDI.Enki.WebApi/Program.cs:205-208` — `IdentitySeedConstants.WebApiScope = "enki"` (duplicated to avoid project ref).
- `src/SDI.Enki.Identity/Data/IdentitySeedData.cs:17` — `public const string WebApiScope = "enki"`.
- `src/SDI.Enki.Identity/Data/IdentitySeedData.cs:29` — `EnkiAdminRole = "enki-admin"`.
- `src/SDI.Enki.WebApi/Authorization/CanAccessTenantRequirement.cs:36` — `AdminRole = "enki-admin"`.

**Problem:** Two pairs of constants have to stay in sync. The class comments correctly note this fails closed (auth stops working) — that's safer than failing open, but a single source of truth is better.

**Fix:** Add `src/SDI.Enki.Shared/Identity/AuthConstants.cs` (`Shared` is referenced by both projects):
```csharp
namespace SDI.Enki.Shared.Identity;

public static class AuthConstants
{
    public const string WebApiScope = "enki";
    public const string EnkiAdminRole = "enki-admin";
}
```
Replace the duplicated constants with references to `AuthConstants.WebApiScope` and `AuthConstants.EnkiAdminRole`. Delete the local duplicates and the explanatory comments that justify them.

---

## 17. (Low) `NavMenu` couples to URL string layout

**File:** `src/SDI.Enki.BlazorServer/Components/Layout/NavMenu.razor` (URL-segment parsing around line 110-132)

**Problem:** Determines the active tenant by splitting `NavigationManager.Uri` on `/` and looking for the segment after `tenants`. If routes ever change shape (e.g. `/orgs/{code}/...`) the sidebar silently stops tracking the current tenant.

**Fix:** Add `src/SDI.Enki.BlazorServer/Services/ITenantScopeService.cs`:
```csharp
public interface ITenantScopeService
{
    string? CurrentTenantCode { get; }
    event Action? Changed;
}
```
Implement it by listening to `NavigationManager.LocationChanged` and parsing route data once. Inject into `NavMenu` and pages instead of re-parsing the URL in each component. Keep the URL-parsing logic centralized in the service.

---

## 18. (Low) `ApplicationUser.UserType` is a free-string

**File:** `src/SDI.Enki.Identity/Data/ApplicationUser.cs`, `src/SDI.Enki.Identity/Data/IdentitySeedData.cs:67` (`UserType = "Team"`)

**Problem:** `UserType` is `string`, populated from a hardcoded literal. If user-type semantics matter (`Team` vs `Customer` vs `Service`), losing the type system here will bite the first time a typo lands.

**Fix:** Convert to a SmartEnum like `TenantStatus`:
```csharp
public sealed class UserType : SmartEnum<UserType>
{
    public static readonly UserType Team    = new(nameof(Team), 1);
    public static readonly UserType Customer = new(nameof(Customer), 2);
    public static readonly UserType Service = new(nameof(Service), 3);
    private UserType(string name, int value) : base(name, value) { }
}
```
Map with `HasConversion(v => v.Value, v => UserType.FromValue(v))` in `ApplicationDbContext.OnModelCreating`. Add an EF migration.

---

## 19. (Low) README is 7 bytes; `reset-dev.ps1` undocumented

**Files:** `README.md` (7 bytes), `scripts/reset-dev.ps1`, `docs/TEST_PLAN.md`.

**Problem:** A new dev cloning the repo gets nothing. `reset-dev.ps1` exists with no explanation of what it does, when to run it, what it modifies.

**Fix:** Write `README.md` with at minimum:
- One-paragraph "what is Enki"
- Required tooling versions (.NET 10 SDK, SQL Server connection)
- How to set `EnkiMasterCs`, `Identity:Issuer`, `WebApi:BaseAddress`, etc.
- How to launch the four hosts (Identity, WebApi, BlazorServer, Migrator)
- What `scripts/reset-dev.ps1` does and when to run it
- Pointer to `docs/TEST_PLAN.md`

Keep it short — a working "git clone → dotnet run" path is more valuable than a comprehensive doc.

---

## 20. (Low) `AthenaMasterDbContext` retains legacy "Athena" name

**File:** `src/SDI.Enki.Infrastructure/Data/AthenaMasterDbContext.cs`

**Problem:** "Athena" is the legacy system being replaced. The type lives in the *Enki* infrastructure assembly — the name is misleading to new readers.

**Fix:** Either:
- Rename to `EnkiMasterDbContext` (touches: type name, both design-time factories, `DependencyInjection.cs`, all test fixtures, all hosts that reference it). Mechanical rename via IDE refactor; verify it compiles and tests pass.
- Or add a class-level comment: `/// "Athena" is the durable code-name for the master schema, retained from the legacy system. Not to be confused with the Athena product.`

The first option is cleaner; the second is the one-line band-aid.

---

## Verify before acting

Memory at the start of this review was 30 days old and several details had drifted. Confirm against current code before making large changes:

- **Per-tenant DB pair, not per-job.** Memory described per-job DBs. Current architecture has Active+Archive **per tenant** (see `TenantDatabase` keyed by `(TenantId, Kind)` in `AthenaMasterDbContext.cs:151`). Anything assuming per-job DBs in older docs/tests is wrong.
- **No CQRS/MediatR.** Memory mentioned MediatR. Current controllers query `DbContext` directly. If the next phase reintroduces CQRS, several findings (#7 error shapes, #1 auth) will be easier to fix uniformly.
- **No Ardalis.Specification.** Memory mentioned it. Not present in any `.csproj`. `Ardalis.GuardClauses` and `Ardalis.SmartEnum` are present.
- **Blazor Server, not WASM.** Memory said WASM. Current is `.AddInteractiveServerComponents()` with cookie auth → bearer-token handler bridge to WebApi.
- **OpenIddict, not stock Identity+JWT.** Memory said the latter. Current is `OpenIddict.AspNetCore` (server in Identity host) + `OpenIddict.Validation.AspNetCore` (validator in WebApi host) on top of ASP.NET Identity for user storage.

If you're picking work off this list, run a quick `git log -- <file>` on each cited file to confirm it hasn't moved or been heavily reworked since this report was written.

---

## Suggested execution order

If you can only ship a few items in one PR, this is the minimum-risk order:

1. **PR 1: Auth gap (items #1, #2, #4)** — five-line attribute changes + new test file + remove design-time fallback. Closes the cross-tenant exposure.
2. **PR 2: Seed safety (item #3)** — gate `IdentitySeedData.SeedAsync` on `IsDevelopment()`; move dev password and client secret to config.
3. **PR 3: Robustness (items #5, #6, #11)** — guards, validation, logging.
4. **PR 4: Consistency (items #7, #8, #16)** — error-response standardization, DTO validation, shared auth constants.
5. **PR 5: Performance + caching (items #9, #10, #12)** — cache busting, race handling, N+1 fix.
6. **PR 6: Tests + docs (items #13, #19)** — lifecycle contract test, README.
7. **Backlog: items #14, #15, #17, #18, #20** — larger refactors or design decisions.

Each PR should keep `TreatWarningsAsErrors=true` green and add tests for the behavior it changes.

# Enki — Test Plan (commit `76ab1a4`)

Structured verification of what's been built through Phase 4h. Steps are
ordered bottom-up: if an earlier step fails, later ones probably will too,
so fix before proceeding.

**Already self-verified by the build pipeline:**
- Solution compiles clean (`dotnet build SDI.Enki.slnx` → 0 warnings / 0 errors)
- Unit tests pass (`dotnet test` → 20/20 in `SDI.Enki.Infrastructure.Tests`)
- `dotnet ef migrations add` produces valid migration SQL for every phase

**Not self-verified (this document):** anything that needs a real SQL Server,
a running HTTP endpoint, Marduk computing values against real data, or
tenant-isolation boundary checks.

**Reporting failures:** for any step that fails, paste back (1) the step
number, (2) the command you ran, (3) the full error or unexpected output.
That's enough for me to diagnose.

---

## 0. Prerequisites

- [ ] Dev SQL Server at `10.1.7.50` reachable:
  `sqlcmd -S "10.1.7.50" -E -Q "SELECT @@VERSION"` (`-E` uses Windows auth)
  — if this fails, check network / VPN and SQL auth mode before proceeding.
- [ ] `dotnet --version` reports **10.0.x**
- [ ] `dotnet ef --version` reports **10.0.x**
- [ ] `sqlcmd` or SSMS available for direct DB inspection
- [ ] Master connection string points at `10.1.7.50` in both:
  - `src/SDI.Enki.Migrator/appsettings.Development.json`
  - `src/SDI.Enki.WebApi/appsettings.Development.json`
  Both are committed with `Server=10.1.7.50;Database=Enki_Master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;`.
  Switch to `User Id=…;Password=…` if SQL auth is required.

---

## 1. Master DB — apply migration & verify seed

### 1.1 Apply the master migration

```powershell
cd D:\Mike.King\Workshop\Enki
dotnet ef database update `
  --project src\SDI.Enki.Infrastructure `
  --context AthenaMasterDbContext
```

**Expected:** `Done.` with no errors. Connection string comes from the
design-time factory (`AthenaMasterDbContextFactory.cs`) which defaults to
LocalDB — override with `--connection "..."` if you use a different server.

### 1.2 Verify master tables & seed in SSMS

```sql
USE Enki_Master;

SELECT name FROM sys.tables ORDER BY name;
-- Expected 11 (+ __EFMigrationsHistory):
--   Calibration, MigrationRun, Setting, SettingUser, Tenant,
--   TenantDatabase, TenantUser, Tool, User, UserTemplate, UserUserTemplate

SELECT COUNT(*) FROM [User];                 -- 12
SELECT COUNT(*) FROM UserTemplate;            -- 3
SELECT COUNT(*) FROM UserUserTemplate;        -- 18

SELECT Name FROM UserTemplate ORDER BY Id;
-- All Team Access / Technical Team Access / Senior Team Access

SELECT Id FROM [User] WHERE Name = 'mike.king';
-- f5fd1207-1dc6-49c7-a794-b5420bd88008

-- Unique index on Tenant.Code:
SELECT is_unique FROM sys.indexes
WHERE object_id = OBJECT_ID('Tenant') AND name LIKE 'IX_Tenant_Code';
-- is_unique = 1
```

- [ ] All checks pass

---

## 2. Migrator CLI — provision a test tenant

### 2.1 Help works without config

```powershell
dotnet run --project src\SDI.Enki.Migrator -- help
```

- [ ] Usage text printed, exit code 0

### 2.2 Provision a tenant end-to-end

```powershell
dotnet run --project src\SDI.Enki.Migrator -- provision `
  --code TEST01 --name "Test Client 01" --region "Sandbox"
```

- [ ] Output shows `Tenant provisioned: TEST01`, an `Id:` GUID, Active DB
  `Enki_TEST01_Active`, Archive DB `Enki_TEST01_Archive (READ_ONLY)`, and
  a Schema version.
- [ ] Exit code 0

### 2.3 Verify physical state in SSMS

```sql
-- Both DBs exist, Archive is read-only:
SELECT name, is_read_only, state_desc
FROM sys.databases
WHERE name IN ('Enki_TEST01_Active', 'Enki_TEST01_Archive');
-- Expected 2 rows. Active: is_read_only=0. Archive: is_read_only=1.

-- Master registry reflects it:
USE Enki_Master;
SELECT Code, Name, Status FROM Tenant WHERE Code='TEST01';
-- Status = 1 (Active)

SELECT Kind, DatabaseName, Status, SchemaVersion
FROM TenantDatabase
WHERE TenantId = (SELECT Id FROM Tenant WHERE Code='TEST01');
-- Two rows. Kind 1 (Active) + Kind 2 (Archive). Both Status=2 (Active).
-- SchemaVersion populated with a migration id.

-- Audit rows:
SELECT Kind, Status, TargetVersion, Error
FROM MigrationRun
WHERE TenantId = (SELECT Id FROM Tenant WHERE Code='TEST01')
ORDER BY StartedAt;
-- Two rows with Status=2 (Success), Error=NULL.
```

- [ ] All checks pass

### 2.4 Verify tenant DB has all the expected tables

```sql
USE Enki_TEST01_Active;
SELECT COUNT(*) FROM sys.tables;
-- Expected ≥ 41 (tenant tables) + 1 (__EFMigrationsHistory) = 42.
-- If close but not exact, list them:
SELECT name FROM sys.tables ORDER BY name;
```

- [ ] Table count matches (or is close; send the list back if unexpected)

### 2.5 Verify CHECK constraints present

```sql
USE Enki_TEST01_Active;
SELECT name FROM sys.check_constraints ORDER BY name;
-- Expected: CK_Shots_ExactlyOneParent, CK_Loggings_ExactlyOneRun
```

- [ ] Both present

### 2.6 Verify unique indexes on lookup tables

```sql
USE Enki_TEST01_Active;

SELECT i.name, i.is_unique
FROM sys.indexes i
WHERE i.object_id IN (OBJECT_ID('Magnetics'), OBJECT_ID('Calibrations'))
  AND i.is_unique = 1 AND i.is_primary_key = 0;
-- Expected 2 rows (one per table, the natural-key unique index)
```

- [ ] Both unique indexes present

### 2.7 Duplicate provision rejected

```powershell
dotnet run --project src\SDI.Enki.Migrator -- provision --code TEST01 --name "duplicate"
```

- [ ] Exit code 2, stderr contains `Tenant code 'TEST01' already exists`

---

## 3. Migrator fan-out — idempotency

### 3.1 Re-running `migrate --all` is a no-op

```powershell
dotnet run --project src\SDI.Enki.Migrator -- migrate --all
```

**Expected:** every row reports `[OK ]` and a migration id; `Summary: N succeeded, 0 failed.`

- [ ] Succeeds with no errors
- [ ] Check `MigrationRun` in master — new rows added per DB with Status=Success

### 3.2 Filter flags work

```powershell
dotnet run --project src\SDI.Enki.Migrator -- migrate --tenants TEST01 --only-active
```

- [ ] Exactly one row in output, for `TEST01/Active`

---

## 4. WebApi — starts & OpenAPI reachable

### 4.1 Launch the WebApi

```powershell
dotnet run --project src\SDI.Enki.WebApi
```

**Expected:** logs show Kestrel listening on `https://localhost:7xxx` and
`http://localhost:5xxx`. Note the HTTPS port — use it for the rest.

- [ ] Starts cleanly, no exceptions in the first 10 seconds

### 4.2 OpenAPI spec accessible

In another shell:
```powershell
curl -k https://localhost:7xxx/openapi/v1.json | Select-String "/tenants"
```

- [ ] Document returned, mentions `/tenants` paths

Leave the WebApi running for the next sections.

---

## 5. Master-level endpoints — Tenants

Define a base URL shortcut:
```powershell
$api = "https://localhost:7xxx"    # replace with your port
```

### 5.1 List tenants

```powershell
curl -k "$api/tenants"
```

- [ ] JSON array with `TEST01` present

### 5.2 Get tenant detail

```powershell
curl -k "$api/tenants/TEST01"
```

- [ ] Detail JSON with `activeDatabaseName = "Enki_TEST01_Active"`, `archiveDatabaseName = "Enki_TEST01_Archive"`, non-null `schemaVersion`

### 5.3 Unknown tenant → 404

```powershell
curl -k "$api/tenants/DOES_NOT_EXIST"
```

- [ ] 404 Not Found

### 5.4 Provision via API

```powershell
curl -k -X POST "$api/tenants" -H "Content-Type: application/json" `
  -d '{"code":"TEST02","name":"Test Client 02","region":"Sandbox"}'
```

- [ ] 201 Created, response includes a GUID and both DB names
- [ ] In SSMS: `Enki_TEST02_Active` and `Enki_TEST02_Archive` exist, Archive is read-only

---

## 6. Tenant routing middleware

### 6.1 Valid tenant routes resolve

```powershell
curl -k "$api/tenants/TEST01/jobs"
```

- [ ] `[]` (empty array), 200 OK

### 6.2 Unknown tenant returns middleware 404

```powershell
curl -k "$api/tenants/DOES_NOT_EXIST/jobs"
```

- [ ] 404 with JSON body `{"error":"Tenant 'DOES_NOT_EXIST' not found."}`

---

## 7. Tenant-scoped CRUD happy path

Use `TEST01` for everything below.

### 7.1 Create a Job

```powershell
curl -k -X POST "$api/tenants/TEST01/jobs" -H "Content-Type: application/json" `
  -d '{"name":"Alpha-3H","description":"first real job","units":"Imperial","wellName":"Alpha 3H"}'
```

- [ ] 201 Created; note the `id` (int) for later — call it `$jobId`

### 7.2 Create a Well

```powershell
curl -k -X POST "$api/tenants/TEST01/wells" -H "Content-Type: application/json" `
  -d '{"name":"Alpha 3H","type":"Target"}'
```

- [ ] 201 Created; note the well `id` → `$wellId`

### 7.3 Create a Gradient Run

```powershell
curl -k -X POST "$api/tenants/TEST01/jobs/$jobId/runs" -H "Content-Type: application/json" `
  -d '{"name":"Grad Run 1","description":"primary","type":"Gradient","startDepth":0,"endDepth":2000,"bridleLength":15,"currentInjection":5}'
```

- [ ] 201 Created; note the run `id` (GUID) → `$runId`

### 7.4 Create a Gradient under the Run

```powershell
curl -k -X POST "$api/tenants/TEST01/jobs/$jobId/runs/$runId/gradients" -H "Content-Type: application/json" `
  -d '{"name":"Primary","order":1}'
```

- [ ] 201 Created; note the gradient `id` → `$gradientId`

### 7.5 List everything back

```powershell
curl -k "$api/tenants/TEST01/jobs"
curl -k "$api/tenants/TEST01/wells"
curl -k "$api/tenants/TEST01/jobs/$jobId/runs"
curl -k "$api/tenants/TEST01/jobs/$jobId/runs/$runId/gradients"
```

- [ ] Each returns the created row (or list containing it)

---

## 8. Validation & guards

### 8.1 Bad Run type → 400

```powershell
curl -k -X POST "$api/tenants/TEST01/jobs/$jobId/runs" -H "Content-Type: application/json" `
  -d '{"name":"x","description":"x","type":"Bogus","startDepth":0,"endDepth":0}'
```

- [ ] 400 with `Unknown Run Type 'Bogus'`

### 8.2 Gradient under non-Gradient Run → 400

Create a Rotary run first, then try to add a Gradient under it:
```powershell
curl -k -X POST "$api/tenants/TEST01/jobs/$jobId/runs" -H "Content-Type: application/json" `
  -d '{"name":"Rot","description":"x","type":"Rotary","startDepth":0,"endDepth":100}'
# (note the new runId; call it $rotaryRunId)

curl -k -X POST "$api/tenants/TEST01/jobs/$jobId/runs/$rotaryRunId/gradients" -H "Content-Type: application/json" `
  -d '{"name":"Nope","order":1}'
```

- [ ] 400 with `Run ... is type 'Rotary', not Gradient.`

### 8.3 Nonexistent Job → 404 from Runs list

```powershell
curl -k "$api/tenants/TEST01/jobs/99999/runs"
```

- [ ] 404

### 8.4 Delete a Gradient with child Shots → 409 (we'll set this up in §9)

---

## 9. FindOrCreateAsync — the trigger replacement

This is the single most important behavioural test because it's the
repository-layer replacement for the legacy 17 AFTER-INSERT dedup triggers.

### 9.1 First shot — creates Magnetics row

```powershell
curl -k -X POST "$api/tenants/TEST01/gradients/$gradientId/shots" -H "Content-Type: application/json" `
  -d '{"shotName":"s001","fileTime":"2026-04-24T12:00:00Z","toolUptime":0,"shotTime":0,"timeStart":0,"timeEnd":0,"numberOfMags":4,"frequency":60,"bandwidth":2,"sampleFrequency":1000,"sampleCount":16000,"magnetics":{"bTotal":50000,"dip":60,"declination":5},"calibration":{"name":"Tool-42","calibrationString":"<cal/>"}}'
```

- [ ] 201 Created

In SSMS:
```sql
USE Enki_TEST01_Active;
SELECT COUNT(*) FROM Magnetics;      -- 1
SELECT COUNT(*) FROM Calibrations;   -- 1
SELECT MagneticsId, CalibrationsId FROM Shots;  -- matching the lookup ids
```

### 9.2 Second shot, SAME magnetics/calibration — lookups stay at 1

```powershell
curl -k -X POST "$api/tenants/TEST01/gradients/$gradientId/shots" -H "Content-Type: application/json" `
  -d '{"shotName":"s002","fileTime":"2026-04-24T12:05:00Z","toolUptime":0,"shotTime":0,"timeStart":0,"timeEnd":0,"numberOfMags":4,"frequency":60,"bandwidth":2,"sampleFrequency":1000,"sampleCount":16000,"magnetics":{"bTotal":50000,"dip":60,"declination":5},"calibration":{"name":"Tool-42","calibrationString":"<cal/>"}}'
```

- [ ] 201 Created
- [ ] `SELECT COUNT(*) FROM Magnetics` = **still 1** ✅ (the trigger replacement is working)
- [ ] `SELECT COUNT(*) FROM Calibrations` = **still 1**
- [ ] Both Shots share the same `MagneticsId` and `CalibrationsId`

### 9.3 Third shot, DIFFERENT magnetics → new row

```powershell
curl -k -X POST "$api/tenants/TEST01/gradients/$gradientId/shots" -H "Content-Type: application/json" `
  -d '{"shotName":"s003","fileTime":"2026-04-24T12:10:00Z","toolUptime":0,"shotTime":0,"timeStart":0,"timeEnd":0,"numberOfMags":4,"frequency":60,"bandwidth":2,"sampleFrequency":1000,"sampleCount":16000,"magnetics":{"bTotal":50001,"dip":60,"declination":5}}'
```

- [ ] `SELECT COUNT(*) FROM Magnetics` = **2** (new distinct row)

### 9.4 Shot GET round-trip

```powershell
curl -k "$api/tenants/TEST01/shots/1"
```

- [ ] Full ShotDetail JSON; `magnetics` object present; `gradientId` populated; `rotaryId` null

### 9.5 Delete-with-children guard

```powershell
curl -k -X DELETE "$api/tenants/TEST01/jobs/$jobId/runs/$runId/gradients/$gradientId"
```

- [ ] 409 Conflict, JSON error `Gradient has child Shots; delete or reparent them first.`

---

## 10. DB-level CHECK constraints (SSMS direct)

These aren't reachable through controllers — we fire raw INSERTs to confirm
the DB rejects malformed rows as the last line of defence.

### 10.1 CK_Shots_ExactlyOneParent

```sql
USE Enki_TEST01_Active;

-- Both parents null — should FAIL with CHECK constraint violation:
INSERT INTO Shots (ShotName, FileTime, ToolUptime, ShotTime, TimeStart, TimeEnd,
                   NumberOfMags, Frequency, Bandwidth, SampleFrequency)
VALUES ('bad', SYSDATETIMEOFFSET(), 0, 0, 0, 0, 0, 0, 0, 0);
-- Expected: Msg 547, "CK_Shots_ExactlyOneParent"
```

- [ ] Error 547 with constraint name

```sql
-- Both parents non-null — should also FAIL:
INSERT INTO Shots (ShotName, FileTime, ToolUptime, ShotTime, TimeStart, TimeEnd,
                   NumberOfMags, Frequency, Bandwidth, SampleFrequency,
                   GradientId, RotaryId)
VALUES ('bad', SYSDATETIMEOFFSET(), 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
-- Expected: the same error
```

- [ ] Error 547

### 10.2 CK_Loggings_ExactlyOneRun

```sql
USE Enki_TEST01_Active;

-- Zero run FKs — FAIL:
INSERT INTO Loggings (ShotName, FileTime, CalibrationId, MagneticId, LogSettingId)
VALUES ('bad', SYSDATETIMEOFFSET(), 0, 0, 0);
-- Expected: Msg 547, "CK_Loggings_ExactlyOneRun"
```

- [ ] Error 547

### 10.3 Unique index on Magnetics

```sql
USE Enki_TEST01_Active;

-- Duplicate natural key — FAIL:
INSERT INTO Magnetics (BTotal, Dip, Declination) VALUES (50000, 60, 5);
-- Expected: Msg 2601 or 2627 (unique index violation)
```

- [ ] Error 2601 or 2627

---

## 11. Marduk integration — survey calculation

### 11.1 Seed a TieOn + Surveys (no controllers for these yet)

```sql
USE Enki_TEST01_Active;
DECLARE @wid INT = (SELECT TOP 1 Id FROM Wells WHERE Name='Alpha 3H');

INSERT INTO TieOns (WellId, Depth, Inclination, Azimuth, North, East,
                    Northing, Easting, VerticalReference,
                    SubSeaReference, VerticalSectionDirection)
VALUES (@wid, 0, 0, 0, 0, 0, 1000000, 500000, 100, 50, 0);

-- Straight vertical, then a gradual build:
INSERT INTO Surveys (WellId, Depth, Inclination, Azimuth, VerticalDepth, SubSea,
                     North, East, DoglegSeverity, VerticalSection,
                     Northing, Easting, Build, Turn)
VALUES
  (@wid,  100,  0,  0, 0,0,0,0,0,0,0,0,0,0),
  (@wid,  500,  0,  0, 0,0,0,0,0,0,0,0,0,0),
  (@wid, 1000, 10,  0, 0,0,0,0,0,0,0,0,0,0),
  (@wid, 1500, 30, 45, 0,0,0,0,0,0,0,0,0,0),
  (@wid, 2000, 50, 90, 0,0,0,0,0,0,0,0,0,0);
```

### 11.2 Run the calculator

```powershell
curl -k -X POST "$api/tenants/TEST01/jobs/$jobId/wells/$wellId/surveys/calculate" `
  -H "Content-Type: application/json" `
  -d '{"metersToCalculateDegreesOver":30,"precision":6}'
```

- [ ] 200 OK, response `surveysProcessed: 5`, `precision: 6`

### 11.3 Verify trajectory values in SSMS

```sql
USE Enki_TEST01_Active;
SELECT Depth, Inclination, Azimuth,
       VerticalDepth, North, East, DoglegSeverity, Build, Turn
FROM Surveys
WHERE WellId = (SELECT TOP 1 Id FROM Wells WHERE Name='Alpha 3H')
ORDER BY Depth;
```

- [ ] First two rows (vertical): `VerticalDepth ≈ Depth`, `North ≈ 0`, `East ≈ 0`
- [ ] Last rows: `North` and `East` grow as Inclination increases and Azimuth bends northeast
- [ ] `DoglegSeverity` non-zero between rows 2→3 and 3→4 (where inclination jumps)
- [ ] `Build` positive where inclination is increasing
- [ ] No NaN / ridiculous values

If the values look wrong, the Marduk mapping is suspect — report back.

---

## 12. Tenant isolation smoke test

The critical security guarantee. A tenant A user must not see any of tenant B's
data through any URL path.

### 12.1 Put data in TEST02

```powershell
curl -k -X POST "$api/tenants/TEST02/jobs" -H "Content-Type: application/json" `
  -d '{"name":"Beta-1H","description":"tenant-B job","units":"Imperial"}'
```

### 12.2 Confirm it's NOT visible via TEST01

```powershell
curl -k "$api/tenants/TEST01/jobs"
```

- [ ] `Alpha-3H` present; `Beta-1H` absent

### 12.3 Confirm it IS visible via TEST02

```powershell
curl -k "$api/tenants/TEST02/jobs"
```

- [ ] `Beta-1H` present; `Alpha-3H` absent

### 12.4 Direct SSMS confirmation (belt and suspenders)

```sql
USE Enki_TEST01_Active; SELECT Name FROM Job;
-- Expected: only Alpha-3H

USE Enki_TEST02_Active; SELECT Name FROM Job;
-- Expected: only Beta-1H
```

- [ ] Physical isolation confirmed

---

## 13. Cleanup (optional)

When you're done:

```sql
-- Master side — remove both tenants + their DB rows:
USE Enki_Master;
DELETE FROM TenantDatabase WHERE TenantId IN (SELECT Id FROM Tenant WHERE Code IN ('TEST01','TEST02'));
DELETE FROM TenantUser     WHERE TenantId IN (SELECT Id FROM Tenant WHERE Code IN ('TEST01','TEST02'));
DELETE FROM MigrationRun   WHERE TenantId IN (SELECT Id FROM Tenant WHERE Code IN ('TEST01','TEST02'));
DELETE FROM Tenant         WHERE Code IN ('TEST01','TEST02');

-- Physical DBs — drop them:
DROP DATABASE Enki_TEST01_Active;
DROP DATABASE Enki_TEST01_Archive;
DROP DATABASE Enki_TEST02_Active;
DROP DATABASE Enki_TEST02_Archive;
```

---

## Summary checklist

Copy-paste and tick as you go:

```
[ ] 0. Prerequisites
[ ] 1. Master DB migration + seed
[ ] 2. Provision TEST01 (Active + Archive, Archive READ_ONLY, audit rows)
[ ] 3. Migrator fan-out idempotent + filter flags work
[ ] 4. WebApi starts + OpenAPI reachable
[ ] 5. /tenants endpoints (list, get, provision, 404 on unknown)
[ ] 6. Tenant routing middleware (valid vs unknown)
[ ] 7. Happy-path CRUD (Job → Well → Run → Gradient)
[ ] 8. Validation & guards (bad run type, wrong-type parent, missing ids)
[ ] 9. FindOrCreateAsync dedup (1 row stays 1 row)
[ ] 10. CHECK constraints reject bad rows at DB level
[ ] 11. Marduk survey calculation produces sensible trajectory
[ ] 12. Tenant isolation (A can't see B's data)
```

Any step failing — paste the step number + command + error back and I'll
diagnose and patch.

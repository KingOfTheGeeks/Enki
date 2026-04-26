# Enki — Surveys & Import Test Plan

Step-by-step UI + API verification for the work shipped between
`85d30bb` and `4f099a2`:

- File-level survey import (CSV / TSV / whitespace / LAS)
- Bulk-clear surveys
- Inline tie-on edit as the first row of the surveys grid
- Tie-on overwrite conflict prompt (existing non-default tie-on protection)
- Auto-recalc on every survey / tie-on mutation (server-side)
- Depth-0-first-row → tie-on promotion in the importer
- Metric-only seed values, GUI conversion at the boundary
- **Four-tenant demo seed** with original company names — PERMIAN,
  BAKKEN (Field), NORTHSEA, CARNARVON (Metric)
- **Units display layer** wired through every Wells-area grid + form
  (headers, cells, edit templates, stat cards) — sections 13.1–13.9
- **Sidebar drill-in breadcrumb** — Job + Well below Jobs as you
  descend — section 14
- **CommonMeasure** treated as a dimensionless signal-calc multiplier
  (≈ 1.0), no longer mud weight — section 15

It's intentionally written as a checklist a human tester (Gavin) can
run end-to-end with no extra context. **Tick boxes as you go**; if a
step fails, note (1) which step, (2) what you saw, (3) any error from
the WebApi or Blazor windows.

> ℹ Convention used below: `(Gavin)` means a step is best run signed
> in as Gavin to exercise the cross-tenant admin path; `(Mike)` means
> sign in as Mike. Most steps work as either user — the labels matter
> only when the test is specifically about access.

---

## 0. Prerequisites

- [ ] **Build clean.** From the repo root:
  ```powershell
  cd D:\Mike.King\Workshop\Enki
  dotnet build --nologo
  ```
  Expected: `0 Error(s)`. Warnings are pre-existing.

- [ ] **Tests green.**
  ```powershell
  dotnet test --no-build --nologo --verbosity quiet
  ```
  Expected: **248 / 248 passed** (71 + 22 + 152 + 3).

- [ ] **AMR.Core.IO tests green** (Marduk repo).
  ```powershell
  cd D:\Mike.King\Workshop\Marduk\Marduk
  dotnet test Tests/AMR.Core.IO.Tests/AMR.Core.IO.Tests.csproj --nologo --verbosity quiet
  ```
  Expected: **82 / 82 passed**.

- [ ] **Dev SQL Server reachable** at `10.1.7.50`:
  ```powershell
  sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "SELECT @@VERSION"
  ```

- [ ] **Hosts running with a fresh DB.** This wipes all `Enki_*`
  databases so the seeders re-run with the latest schema + roster
  (including Gavin's new admin flag):
  ```powershell
  powershell -ExecutionPolicy Bypass -File D:\Mike.King\Workshop\Enki\scripts\start-dev.ps1 -Reset
  ```
  Wait until all three terminal windows settle:
  - Identity → `Now listening on: http://localhost:5196`
  - WebApi → `Now listening on: http://localhost:5107`
  - Blazor → `Now listening on: http://localhost:5073`

- [ ] **Browser reachable**: `http://localhost:5073` → Enki sign-in page.
  Hard-refresh (Ctrl-F5) to bypass any cached scripts / CSS.

---

## 1. Authentication & user roster

### 1.1 Sign in as Mike (admin)

- [ ] On the sign-in page, enter `mike.king` / `Enki!dev1` → land on Home.
- [ ] Top-right shows `mike.king` as the current user.
- [ ] Sidebar shows `ADMIN` section with `Tenants` and `Admin` entries.

### 1.2 Sign in as Gavin (admin — newly added)

- [ ] Sign out (top-right `SIGN OUT`).
- [ ] Sign in as `gavin.helboe` / `Enki!dev1`.
- [ ] Top-right shows `gavin.helboe`.
- [ ] Sidebar shows `ADMIN` section (Gavin has the same admin reach
  as Mike — `IsEnkiAdmin: true` was flipped in `SeedUsers.cs`).
- [ ] Click `Tenants` → list loads → click `PERMIAN` row → lands
  on `/tenants/PERMIAN/jobs`. (Confirms cross-tenant admin bypass
  works: no per-tenant `TenantUser` row needed for an admin.)

### 1.3 Verify the user roster in the master DB

- [ ] Confirm Gavin and Mike are both flagged admin in Identity:
  ```powershell
  sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "USE Enki_Identity; SELECT UserName, IsEnkiAdmin FROM AspNetUsers WHERE UserName IN ('mike.king','gavin.helboe') ORDER BY UserName"
  ```
  Expected: both rows have `IsEnkiAdmin = 1`.

---

## 2. Navigation — Tenants → Jobs → Wells → Surveys

### 2.0 Tenant roster

- [ ] On `Tenants`, the list shows **four demo tenants** in this order
  (deliberately split 2 × 2 across Field / Metric so the units
  display layer gets exercised on every login):
  - `PERMIAN` — Permian Crest Energy / Permian Basin (Field)
  - `BAKKEN` — Bakken Ridge Petroleum / Williston Basin (Field)
  - `NORTHSEA` — Brent Atlantic Drilling / North Sea — UKCS (Metric)
  - `CARNARVON` — Carnarvon Offshore Pty / NW Shelf — Carnarvon Basin (Metric)
- [ ] Each tenant has Active + Archive databases provisioned. Verify
  via SQL:
  ```powershell
  sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "SELECT name FROM sys.databases WHERE name LIKE 'Enki_%' ORDER BY name"
  ```
  Expected: 10 rows — `Enki_Identity`, `Enki_Master`, plus
  Active+Archive for each of PERMIAN / BAKKEN / NORTHSEA / CARNARVON
  (8 tenant DBs).

### 2.1 Tenant click-through

- [ ] From `Tenants`, clicking the `PERMIAN` row jumps **straight to
  `/tenants/PERMIAN/jobs`** (no intermediate detail page).
- [ ] Click `BAKKEN` → `/tenants/BAKKEN/jobs` shows `Ridge-25-3H`.
- [ ] Click `NORTHSEA` → `/tenants/NORTHSEA/jobs` shows `Atlantic-26-7H`.
- [ ] Click `CARNARVON` → `/tenants/CARNARVON/jobs` shows `Shelf-27-9H`.

### 2.1.1 Per-tenant wells (spec-driven names)

Click each tenant's job → Wells. Verify the well names match the
spec — confirms `TenantSeedSpec` flowed end-to-end:

- [ ] **PERMIAN**: `Lone Star 14H` (Target), `Lone Star 14I` (Injection),
  `Caprock Federal 7` (Offset)
- [ ] **BAKKEN**: `Lambert 2H`, `Lambert 2I`, `Pearson 1`
- [ ] **NORTHSEA**: `Brent A-12`, `Brent A-13`, `Brent A-7`
- [ ] **CARNARVON**: `Gorgon 9H`, `Gorgon 9I`, `Pluto 3`

### 2.1.2 Per-tenant surface coordinates

Each tenant's wells should sit at distinct Northing / Easting (the
spec's `SurfaceNorthing` / `SurfaceEasting` plus the relative offsets
for injector / offset). Open `Crest-22-14H` → `Lone Star 14H` →
Surveys, then jump to each of the other three tenants' equivalents.
The tie-on row's Northing column should differ visibly between
tenants:

- [ ] **PERMIAN** target tie-on: Northing ≈ `457200`
- [ ] **BAKKEN** target tie-on: Northing ≈ `5300000`
- [ ] **NORTHSEA** target tie-on: Northing ≈ `6700000`
- [ ] **CARNARVON** target tie-on: Northing ≈ `7550000`

### 2.2 Job click-through

- [ ] On the Jobs grid, click `Crest-22-14H` → lands on the JobDetail
  page. The detail card shows tenant + job info; a "Wells" stat card
  shows the count `03`.

### 2.3 Wells list

- [ ] Click the Wells card → `/wells` lists three wells: `Lone Star 14H`,
  `Lone Star 14I`, `Caprock Federal 7`.
- [ ] **The Tie-ons column is gone from the wells list** (only Name,
  Type, Surveys, Created remain — verify by horizontal scroll).
  *(If you still see a `Tie-ons` column, you're on a stale build.)*

### 2.4 Well detail

- [ ] Click `Lone Star 14H` → lands directly on the **Surveys page**
  (default view for a well, like Tenants → Jobs).
- [ ] Click `Back to well` → lands on `WellDetail`.
- [ ] Verify the WellDetail's child-entity cards: **Surveys, Tubulars,
  Formations, Common Measures** (4 cards). **No Tie-Ons card.**

---

## 3. Surveys grid — read-only smoke test

> Expected values quoted in this section are the **Field-display
> values you'll see on PERMIAN** (the bootstrap demo, units = ft).
> Same numbers in metres on NORTHSEA / CARNARVON — multiply Field
> ft by `0.3048` for the Metric expectation. Section 13 covers the
> unit-layer behaviour in depth.

### 3.1 Grid layout

- [ ] On `Lone Star 14H` Surveys, the page shows:
  - **Three stat cards** (Stations / Min depth (ft) / Max depth (ft))
    — no "Calculated" card.
  - **Header buttons**: Back to well · Import file · Clear surveys ·
    Edit tie-on / Advanced tie-on · + New survey.
  - **Grid** with the tie-on as the first row (depth `0.00`),
    followed by 10 survey rows from MD ≈ `1,000.00` ft to ≈ `10,000.00`
    ft.
- [ ] **No filter row** under the column headers (no red squiggle).
- [ ] **Pager** at the bottom: shows page-size selector (25 default)
  + Goto-page controls.
- [ ] **No "Calculated" stat card** at the top (auto-calc means
  always-yes — removed as redundant).

### 3.2 Pager

- [ ] With 10 surveys + 1 tie-on row, single page (page 1 of 1) — no
  paging controls beyond a single page button.
- [ ] Switch page-size to 10 → grid splits across 2 pages — `Next`
  reaches page 2 showing the remaining row(s).
- [ ] Switch back to 25.

### 3.3 No user sorting

- [ ] Click any column header → **nothing happens** (sort disabled —
  depth-ascending is the only meaningful order; the API delivers it
  pre-sorted).

### 3.4 Computed columns populated

(All values below are PERMIAN / Field display in ft. Metric tenants
show the same data in metres — same rows, same trajectory.)

- [ ] First survey row at MD ≈ `1,000.00` ft: TVD ≈ `1,000.00` ft,
  North ≈ `-4.36` ft, Northing ≈ `1,499,995.65` ft, DLS ≈ `0.05`
  °/100ft, V-sect ≈ negative-something (build to lateral going south).
- [ ] Last survey row at MD ≈ `10,000.00` ft: TVD ≈ `4,795.25` ft
  (lateral held at ~4,795 ft TVD; matches the ISCWSA Well-3
  horizontal profile within rounding).
- [ ] **The auto-calc invariant**: Northing values monotonically
  decrease (well drills southward, az 180°); North values are negative
  and monotonically more negative; East stays at `0.00`.

---

## 4. Tie-on inline edit (Syncfusion grid)

### 4.1 Enter edit mode

- [ ] **Double-click any cell on the tie-on row** (the first row, MD
  `0.00`). The row enters edit mode:
  - Editable cells become text inputs (Depth / Inc / Az / TVD /
    Sub-sea / Northing / Easting).
  - Read-only cells remain flat text (North / East / DLS / V-sect /
    Build / Turn — always 0 for the anchor).
- [ ] **No up/down spinner arrows** on any of the inputs (the global
  CSS in `enki-theme.css` strips both Syncfusion's `.e-spin-up` /
  `.e-spin-down` and the browser's native `<input type="number">`
  spinners).
- [ ] Toolbar at the top of the grid shows **Update** and **Cancel**.

### 4.2 Auto-recalc on save

- [ ] In the tie-on's Northing cell, change `457200.00` to `457250.00`.
- [ ] Press Enter (or click Update in the toolbar).
- [ ] Page reloads. Verify in the grid:
  - Tie-on Northing now reads `457250.00`.
  - **Every survey row's Northing has shifted by +50** (the auto-calc
    re-anchored the trajectory to the new Northing).
  - North values shift correspondingly (since `North = Northing -
    tieOn.Northing` in the engine).
- [ ] Edit again, restore Northing to `457200.00`, save. Surveys
  return to original Northings.

### 4.3 Cancel discards changes

- [ ] Double-click the tie-on row again. Change Depth from `0.00` to
  `999.99`.
- [ ] Click **Cancel** in the toolbar (or press Esc). The page does
  *not* reload; the Depth value reverts to `0.00`.
- [ ] No PUT was sent — verify by checking the WebApi window: no
  `PUT /tieons/...` log line for this attempt.

### 4.4 Survey rows are NOT editable

- [ ] Double-click any **survey** row (e.g. MD `304.80`). Nothing
  happens — `OnActionBegin` cancels the edit because `IsTieOn == false`.

### 4.5 Advanced tie-on link

- [ ] Click `Advanced tie-on` in the page header → navigates to
  `/tieons/{id}/edit` (the dedicated full-field tie-on editor — for
  rare fields like VSD that aren't in the grid).
- [ ] Click `Back to surveys` (or the surveys link) to return.

---

## 5. Tie-on creation when none exists

This path requires a well with no tie-on. Easy way to set up: clear
all tie-ons on a chosen well via SQL, then refresh.

- [ ] Pick the offset well (`Caprock Federal 7`) and wipe its tie-ons:
  ```powershell
  sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "USE Enki_PERMIAN_Active; DELETE FROM TieOn WHERE WellId IN (SELECT Id FROM Well WHERE Name = 'Caprock Federal 7')"
  ```
- [ ] In the browser, navigate to the offset well's Surveys page (or
  refresh). You should see:
  - The grid has no tie-on row at the top — only the survey rows
    (MDs `304.80`, `914.40`, `1524.00`, `2133.60`, `2438.40`).
  - The header button reads **`+ Create tie-on`** instead of
    `Edit tie-on` / `Advanced tie-on`.
  - Survey rows have **zero** computed columns (TVD = 0, North = 0,
    etc.) because the auto-calc no-ops without an anchor.
- [ ] Click `+ Create tie-on`. Page reloads.
- [ ] Tie-on row appears at depth `0.00`. Survey rows now have
  computed values populated (auto-calc fired after the POST).
- [ ] Header button is now `Edit tie-on`.

---

## 6. Survey file import — happy paths

Sample files live at `D:\Mike.King\Workshop\Enki\samples\survey-imports\`.
The README in that folder is the spec for what each one tests.

> **Note on numbers below:** the depth / TVD values in this section
> are **SI-as-stored (m)**. The grid will project them into the
> active tenant's preset for display. On a Field tenant
> (`PERMIAN` / `BAKKEN`) you'll see feet on screen — multiply the
> quoted m values by `3.28084` for what you'll actually see. On
> Metric tenants the screen value matches the quote directly. To
> sanity-check against storage you can run the SQL in section 12.

### 6.1 Simple metric CSV

- [ ] On any well's Surveys page, click **Import file**.
- [ ] Pick `01-vertical-well-metric.csv`. Wait for the upload.
- [ ] Page reloads with:
  - Tie-on row at depth `0.00` (file's first row).
  - 5 survey rows: MD `300`, `600`, `900`, `1200`, `1500`.
  - Computed columns populated (TVD ≈ MD for a near-vertical well).
- [ ] Below the button, an **Import notes** disclosure shows
  `[Warning] TIEON_FROM_FIRST_ROW` (and possibly `UNIT_DEFAULT_USED`
  if no header unit was detected).
- [ ] Stat cards update: Stations = `05`, Min depth = `300`, Max
  depth = `1500`.

### 6.2 Imperial CSV (feet → meters auto-conversion)

- [ ] **Import file** → `02-horizontal-lateral-feet.csv`.
- [ ] Page reloads. Verify:
  - Tie-on at depth `0.00` (file's `0` ft = `0` m).
  - Last survey row depth ≈ `3048.00` (file's `10000` ft × 0.3048).
  - Inc column shows `90.00` for the lateral rows (file held at 90°
    over MDs `7000`–`10000` ft = `2133.6`–`3048` m).
  - Computed TVD at the deepest row ≈ `1700–1800` m (horizontal
    lateral; vertical depth held).
- [ ] Import notes show `[Warning] TIEON_FROM_FIRST_ROW` AND
  `[Info / Warning]` indicating the depth unit was detected as feet
  (`DETECTED FROM HEADER`-style message).

### 6.3 Compass-style CSV with metadata + pre-computed columns

- [ ] **Import file** → `03-compass-export-with-metadata.csv`.
- [ ] Page reloads.
- [ ] Surveys grid populated (tie-on at depth 0 + 10 survey rows from
  `200` to `2000`).
- [ ] Computed TVD values from the auto-calc are **close to but not
  identical** to the pre-computed values in the file (pre-computed
  columns are captured for audit but not trusted; the auto-calc runs
  fresh).
- [ ] Verify the well-name harvest in the WebApi log:
  `[INF] Imported … from Csv (detected meters)` or similar — no
  fail-loudly behaviour.

### 6.4 LAS 2.0 file

- [ ] **Import file** → `04-survey-las2.las`.
- [ ] Page reloads. Verify:
  - Tie-on at `0.00`.
  - 8 survey rows (MD `300` through `2400` m, 300m steps).
  - Azimuth column shows `135.00` (NE-bound — different from the
    seed default 180° to make file-source visually obvious).
  - Computed columns populated; North values positive (going NE),
    East values positive.

### 6.5 TSV (tab-delimited)

- [ ] **Import file** → `05-tab-delimited.tsv`.
- [ ] Page reloads. Tie-on + 7 survey rows visible.
- [ ] The Import-notes panel may show
  `[Warning] UNIT_DEFAULT_USED` (no unit declared in this fixture →
  the importer assumed meters and emitted a warning so the user can
  audit it).

### 6.6 Whitespace fixed-column

- [ ] **Import file** → `06-whitespace-fixed-columns.txt`.
- [ ] Page reloads. Tie-on + 8 survey rows.
- [ ] Computed columns populated.

### 6.7 Survey count math

- [ ] After each import the Stations stat card matches the survey
  rows (file's row count minus the depth-0 row promoted to tie-on).

---

## 7. Survey file import — error & edge cases

### 7.1 Unknown format

- [ ] Pick a non-survey file (any random `.txt` with prose / a `.png`
  renamed to `.txt`) and try to import it.
- [ ] Expected: red error banner reading something like
  `Import failed (400): … FORMAT_UNKNOWN …`. No DB changes.

### 7.2 Empty file

- [ ] Create an empty `.csv` (touch / save empty) and import it.
- [ ] Expected: red error banner with `EMPTY_FILE`.

### 7.3 Validation paths inside the importer

These are exercised via the Marduk fixture suite (82 / 82 passing) —
spot-check via the controller with curl if you want a manual sanity:

```powershell
# Negative azimuth normalisation
curl -F "file=@D:\Mike.King\Workshop\Marduk\Marduk\Tests\AMR.Core.IO.Tests\Resources\SurveyImports\Csv\negative-azimuth.csv" `
     "http://localhost:5107/tenants/PERMIAN/jobs/{jobId}/wells/{wellId}/surveys/import"
```

- [ ] Response carries `[Warning] AZIMUTH_NORMALISED` notes.

(Not strictly needed for UI-only verification — covered by the unit
tests.)

---

## 8. Tie-on overwrite conflict prompt

This is the gate that prevents the import from silently clobbering a
curated tie-on.

### 8.1 Setup: curate a tie-on with non-default values

- [ ] Pick `Lone Star 14H` Surveys.
- [ ] Double-click the tie-on row, change Northing to `457250.00`,
  press Update. Page reloads with the new value.

### 8.2 Import a file that carries a tie-on (default behaviour)

- [ ] Click **Import file**, pick `01-vertical-well-metric.csv`.
- [ ] **Conflict prompt appears below the button**:
  - Heading: "Existing tie-on has non-default values"
  - Lists existing values (Northing = `457250.00`) vs imported
    values (the file's depth-0 row → Northing = `0.00`).
  - Three buttons: **Overwrite tie-on** (red), **Keep existing
    tie-on**, **Cancel**.

### 8.3 Keep existing path

- [ ] Click **Keep existing tie-on**. Page reloads.
- [ ] Tie-on Northing is still `457250.00` (preserved).
- [ ] Survey rows are the file's content (5 rows from `01-vertical…`)
  — surveys replaced, tie-on intact.

### 8.4 Overwrite path

- [ ] Curate the tie-on again (Northing back to a non-zero value
  like `999999.00`).
- [ ] Import any depth-0-first-row file again.
- [ ] In the conflict prompt, click **Overwrite tie-on**. Page reloads.
- [ ] Tie-on Northing is now `0.00` (from the file). Surveys replaced.

### 8.5 Cancel path

- [ ] Curate the tie-on (Northing = `123456.00`).
- [ ] Import a file. Conflict prompt appears.
- [ ] Click **Cancel**. The buffered file is discarded; you see
  `Import cancelled — no changes saved.` No DB changes.
- [ ] Verify Northing still `123456.00`.

### 8.6 No prompt when existing is all-zeros

- [ ] Reset to the seed default tie-on (re-create or set Northing /
  Easting / VR / SSR / VSD all to 0).
- [ ] Import a file. **No conflict prompt** — the importer silently
  overwrites because the existing tie-on has no curated values.

---

## 9. Bulk-clear surveys

### 9.1 Arm-then-confirm pattern

- [ ] Click **Clear surveys** in the header. Button label changes
  to `Click again to confirm clear`.
- [ ] Wait several seconds without clicking. Refresh the page. The
  button is back to `Clear surveys` (state didn't persist across
  reload — disarmed automatically).

### 9.2 Actual clear

- [ ] Click **Clear surveys**. Click it again to confirm.
- [ ] Page reloads. Survey rows are empty:
  - Stations = `00`
  - Min / Max depth show `—`
  - Grid shows only the tie-on row (still preserved — Clear scope
    is surveys, not tie-on).
- [ ] Re-import any sample file to restore.

### 9.3 Idempotent on empty

- [ ] After clearing, click **Clear surveys** twice more. The action
  succeeds with no side effects (`DELETE` on an empty collection is
  idempotent — returns 204 NoContent).

---

## 10. Cross-tenant admin verification (Gavin)

### 10.1 Sign in as Gavin

- [ ] Sign out.
- [ ] Sign in as `gavin.helboe` / `Enki!dev1`.

### 10.2 Verify cross-tenant reach

- [ ] Tenants list → click `PERMIAN` → `/jobs`.
- [ ] Open `Crest-22-14H` → `Lone Star 14H` → Surveys.
- [ ] Verify all the actions Mike can do, Gavin can do too: import,
  clear, edit tie-on, create new survey.

### 10.3 If a second tenant exists

If you provision another tenant via the Tenants page, Gavin should
be able to access it without any per-tenant `TenantUser` row. The
admin role bypasses the membership check in
`CanAccessTenantHandler`.

- [ ] Provision a new tenant (e.g. `TENANT2`) as an admin.
- [ ] Sign out → sign in as Gavin → click into `TENANT2` →
  the admin path lets him in.

---

## 11. WebApi log spot-checks

Through any of the above, the WebApi window should show clean log
lines for every interaction. Specifically:

- [ ] Each tie-on save: `PUT /tenants/.../tieons/{id}` followed by
  `200` or `204`. No 500-level errors.
- [ ] Each survey import: `POST /tenants/.../surveys/import` with
  `200` (or `409` on conflict, `400` on parse failure).
- [ ] Each clear: `DELETE /tenants/.../surveys` with `204`.
- [ ] **No** `Could not find 'sfBlazor.…'` errors. (If they reappear,
  hard-refresh the browser — Syncfusion JS is cache-sensitive.)
- [ ] **No** unhandled exceptions in red.

---

## 12. Sanity checks via SQL

Verify the data the UI is showing matches what's in the DB.

```powershell
# Surveys for Lone Star 14H — should be in metric (the rule: DB always SI)
sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "USE Enki_PERMIAN_Active; SELECT TOP 5 Id, Depth, Inclination, Azimuth, VerticalDepth, Northing FROM Survey ORDER BY Depth"
```

- [ ] Depths in metric (`304.8`, `609.6`, etc. — not `1000`, `2000`).
- [ ] VerticalDepth populated (non-zero — proves the auto-calc fired).
- [ ] Northing values consistent with the displayed grid.

```powershell
# Tie-ons — depth 0 baseline
sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "USE Enki_PERMIAN_Active; SELECT WellId, Depth, Inclination, Azimuth, Northing, Easting, VerticalReference FROM TieOn"
```

- [ ] All three seeded tie-ons at Depth `0`.
- [ ] Northing / Easting carry the well's grid location (target ≈
  `457200` / `182880`, injection ≈ `457185` / `182880`, offset ≈
  `457352` / `183002`).

---

## 13. Units display layer

The DB always stores SI (m / kg/m / °/30m / kg/m³). The GUI converts
at display + input. Two operational presets ship in the seed —
**Field** (PERMIAN, BAKKEN) and **Metric** (NORTHSEA, CARNARVON) —
so every test below should be repeated on at least one tenant of
each preset. **Strict-SI** isn't in the seed; if you really need to
exercise it, edit a Job's UnitSystem to "SI" via JobEdit and re-run
the relevant section.

### 13.1 Headers carry the unit (no truncation)

Land on **PERMIAN** → `Crest-22-14H` → `Lone Star 14H` → Surveys.

- [ ] Every numeric column header reads `Label (unit)`. Specifically:
  - `Depth (ft)`, `Inc (°)`, `Az (°)`
  - `TVD (ft)`, `Sub-sea (ft)`
  - `North (ft)`, `East (ft)`
  - `DLS (°/100ft)`, `V-sect (ft)`
  - `Northing (ft)`, `Easting (ft)`
  - `Build (°/100ft)`, `Turn (°/100ft)`
- [ ] No header reads `From MD (...` or `Weight (l...` — the unit is
  always fully visible. Resize the browser narrower and confirm the
  unit suffix never gets clipped (you can drag a column wider via
  the header divider, but min-width prevents auto-shrink past the
  unit).

Now jump to **NORTHSEA** → the same path. Headers should now read:

- [ ] `Depth (m)`, `TVD (m)`, `North (m)`, `Easting (m)`, etc.
- [ ] `DLS (°/30m)`, `Build (°/30m)`, `Turn (°/30m)`

Inclination / Azimuth / VSD always read **`(°)`** regardless of
preset — the DB stores degrees, not radians.

### 13.2 Cell values flip with the preset

On **PERMIAN** Surveys, look at the tie-on row:

- [ ] Depth shows ~`0.00`, Northing shows ~`457,200.00`,
  Easting ~`182,880.00` — **but in feet now** because Field projects
  m → ft on read. So the actual numbers should be (approximately):
  - Northing ≈ `1,500,000` ft
  - Easting ≈ `600,000` ft
  - First survey Depth ≈ `1,000.00` ft (was `304.80 m` in storage)
- [ ] DLS / Build / Turn read 1.016× the metric value (the °/100ft
  conversion factor).

On **NORTHSEA**, the same fields read raw metric:

- [ ] Northing ≈ `6,700,000.00` m, Easting ≈ `460,000.00` m.
- [ ] DLS / Build / Turn read °/30m as stored.

### 13.3 Stat cards honour the preset

On the Surveys page top:

- [ ] **PERMIAN**: "Min depth" / "Max depth" labels read `(ft)` and
  values are in feet (e.g. `Max depth (ft)` ≈ `10,000`).
- [ ] **NORTHSEA**: same labels read `(m)`, values in metres
  (`Max depth (m)` ≈ `3,050`).

### 13.4 Tubular Diameter is `in` / `mm`, never `ft` / `m`

This is the deliberate column-level override.

- [ ] **PERMIAN** Tubulars grid: Diameter header reads `Diameter (in)`,
  values read like `13.375`, `9.625`, `5.500`.
- [ ] **NORTHSEA** Tubulars grid: Diameter header reads
  `Diameter (mm)`, values read like `339.725`, `244.475`, `139.700`
  (mm equivalents of the same diameters).
- [ ] Weight header reads `Weight (lb/ft)` on Field, `Weight (kg/m)`
  on Metric. Sample weights on Field ≈ 68 / 47 / 17 lb/ft.

### 13.5 Round-trip an edit (Field tenant)

On **PERMIAN**, double-click the tie-on row in Surveys.

- [ ] The edit input shows the value in **ft** (e.g. `0`).
- [ ] Type **`100`** in the Depth cell, then press Enter / click
  Update in the toolbar.
- [ ] After save, the row reads `100.00` ft. Refresh the page
  (Ctrl-F5) and confirm it's still `100.00`.
- [ ] Sanity-check the DB: it should be storing **30.48 m**, not 100.

```powershell
sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "USE Enki_PERMIAN_Active; SELECT TOP 1 Depth FROM TieOn"
# Expected: 30.48 (the SI metres)
```

Set Depth back to `0` before continuing so other tests stay sane.

### 13.6 Round-trip an edit (Metric tenant)

On **NORTHSEA**, same drill:

- [ ] Tie-on Depth input shows `0` (m).
- [ ] Type `30.48`, save.
- [ ] DB carries `30.48` m as well — no conversion needed.

```powershell
sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "USE Enki_NORTHSEA_Active; SELECT TOP 1 Depth FROM TieOn"
# Expected: 30.48
```

### 13.7 Forms (create / edit pages) honour the preset

Navigate **PERMIAN** → Surveys → `+ New survey`.

- [ ] Field labels read `Depth (ft)`, `Inclination (°)`, `Azimuth (°)`.
- [ ] Type `5000` in Depth, `45.5` in Inclination, `90` in Azimuth,
  click Create.
- [ ] Back in Surveys, the new row shows `5,000.00` ft / `45.50` /
  `90.00`. DB stores Depth as `1524.00` m.
- [ ] On **NORTHSEA**, repeat with sensible metric values — labels
  read `(m)`, no conversion happens.

### 13.8 Tubular forms honour the in/mm override

On **PERMIAN** → Tubulars → `+ New tubular`:

- [ ] Diameter label reads `Diameter (in)`. Type `9.625`. Save.
  Grid shows `9.625` in.
- [ ] On **NORTHSEA**, label reads `Diameter (mm)`. Type `244.475`.
  Save. Grid shows `244.475` mm.
- [ ] Both tenants store the same SI metre value internally
  (~`0.244475` m).

### 13.9 You should never see radians or pascals

Anywhere in the UI — surveys, tubulars, formations, forms — the
following NEVER appear:

- [ ] No header reads `(rad)`, `(Pa)`, `(K)`.
- [ ] No cell value is in the e-5 / e-9 range that would indicate a
  raw SI projection of pressure / magnetic field.

Strict-SI is reachable only by explicitly setting a Job's UnitSystem
to "SI" via Edit; the seed never picks it.

---

## 14. Sidebar drill-in breadcrumb

The Operations group in the left sidebar should track where you are
inside a tenant — the current Job (when on a Job-detail or deeper
page) and the current Well (when on a Well-detail or deeper page)
appear as indented entries below "Jobs".

### 14.1 No breadcrumb at the Jobs list

- [ ] Land on **PERMIAN** → Jobs (the list page). The sidebar shows
  Operations [PERMIAN] → Overview, Jobs. **No** sub-items below Jobs.

### 14.2 Job appears at JobDetail

- [ ] Click into `Crest-22-14H`. Sidebar now shows:
  ```
  Operations [PERMIAN]
    Overview
    Jobs
     ─ Crest-22-14H        ← new sub-item, dim accent
  ```
- [ ] Both `Jobs` and `Crest-22-14H` are highlighted as active (the
  whole path lights up).

### 14.3 Well appears at WellDetail

- [ ] From JobDetail, click `Lone Star 14H` in the Wells stat card.
  Sidebar now shows:
  ```
  Operations [PERMIAN]
    Overview
    Jobs
     ─ Crest-22-14H
        ─ Lone Star 14H    ← second nesting level, deeper indent
  ```

### 14.4 Breadcrumb persists into leaf pages

From WellDetail, click into Surveys / Tubulars / Formations /
Common Measures.

- [ ] All three sidebar entries (Jobs, Crest-22-14H, Lone Star 14H)
  remain visible AND active as you move between leaf pages. The
  rail markers stay in alignment.

### 14.5 Breadcrumb collapses when you navigate up

- [ ] From a Surveys page, click `Jobs` in the sidebar — the
  Crest-22-14H + Lone Star 14H entries should disappear.
- [ ] Click `Crest-22-14H` from a leaf page — the Lone Star 14H
  sub-entry disappears, but the Job entry stays.

### 14.6 Sidebar is a "go up" shortcut

- [ ] From Surveys (`.../wells/{n}/surveys`), click the sidebar
  entry for the Well (`Lone Star 14H`). Lands on WellDetail in one
  click — faster than the in-page "Back to well" button.
- [ ] From WellDetail, click sidebar entry for the Job
  (`Crest-22-14H`). Lands on JobDetail.

### 14.7 Tenant switch resets the breadcrumb

- [ ] From a deep PERMIAN Surveys page, click sidebar `Tenants`,
  click **NORTHSEA**, drill into a job. The sidebar should rebuild
  for the new tenant — no PERMIAN job/well entries should leak
  across.

### 14.8 Long names truncate, don't wrap

- [ ] No breadcrumb entry should wrap onto a second line or push the
  sidebar wider than its normal width. Names that don't fit get
  ellipsised at the end (`Permian Crest Energ…`).

---

## 15. Common Measures as signal factor

CommonMeasure was originally seeded as mud weight (kg/m³). It's now
correctly treated as a **dimensionless signal-calculation scaling
multiplier** — a "fudge factor" expressed as a percentage of 1
(typically 0.85 to 1.15, 1.0 = no adjustment).

### 15.1 Grid header + values

Navigate to any tenant → JobDetail → Lead well → Common Measures.

- [ ] The third column header reads exactly `Signal factor` — **no**
  unit suffix, **no** "(mud weight)", **no** `(ppg)` or `(kg/m³)`.
- [ ] The four seeded rows show `0.9500`, `1.0000`, `1.0500`,
  `1.0250` — values clustered around 1.0, NOT in the 1000s as the
  old kg/m³-mud-weight values were.
- [ ] The page subtitle reads "Depth-ranged signal-calculation
  scaling factors — dimensionless multipliers (≈ 1.0)…".

### 15.2 Header description on cards

- [ ] On WellDetail, the Common Measures stat card still reads
  "Common Measures" (4 on each seeded well).
- [ ] No mention of "mud weight" anywhere in the page subtitle.

### 15.3 Create form

Click `+ New measure`.

- [ ] From TVD / To TVD inputs are unit-aware (read `(ft)` on
  PERMIAN, `(m)` on NORTHSEA — same as everywhere else).
- [ ] **Signal factor** field is a bare number input with hint:
  "Dimensionless scaling multiplier used by signal calculations — a
  percentage of 1 (typically 0.85 to 1.15). 1.0 = no adjustment."
- [ ] Type `1.075`, save. Grid shows `1.0750` — same value, no
  conversion either way.

### 15.4 Edit round-trip

- [ ] Click into a row, change the signal factor to `0.97`, save.
- [ ] Grid shows `0.9700`. DB stores `0.97`:

```powershell
sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "USE Enki_PERMIAN_Active; SELECT TOP 1 Value FROM CommonMeasure ORDER BY Id DESC"
```

---

## 16. Reporting

For any failure:

1. **Step number** (e.g. `4.2`)
2. **What you saw** (paste of the screen text or a brief description)
3. **Browser console** (F12 → Console tab) — copy any red errors
4. **WebApi window text** for the last 30 seconds (the Serilog output
   captures the request that triggered it)

That's enough for a precise diagnosis.

---

## Appendix A — Account credentials (dev)

| Username | Password | Admin |
|---|---|---|
| `mike.king` | `Enki!dev1` | yes |
| `gavin.helboe` | `Enki!dev1` | **yes** (newly granted) |
| `dapo.ajayi`, `jamie.dorey`, `adam.karabasz`, `douglas.ridgway`, `travis.solomon`, `james.powell`, `joel.harrison`, `scott.brandel`, `john.borders` | `Enki!dev1` | no |

Default password for every seeded user is `Enki!dev1` (configurable
via `Identity:Seed:DefaultUserPassword`).

## Appendix B — Sample-file inventory

Same files referenced in section 6, with what each one specifically
exercises:

| File | Format | Tests |
|---|---|---|
| `01-vertical-well-metric.csv` | CSV (m) | Smoke; depth-0 → tie-on |
| `02-horizontal-lateral-feet.csv` | CSV (ft) | Header-unit detection + ft→m conversion |
| `03-compass-export-with-metadata.csv` | CSV with comments | Comment-line metadata harvest + pre-computed columns |
| `04-survey-las2.las` | LAS 2.0 | Sectioned format; well metadata extraction |
| `05-tab-delimited.tsv` | TSV | Tab delimiter detection; default-unit warning |
| `06-whitespace-fixed-columns.txt` | Whitespace | Runs-of-whitespace detection |

## Appendix C — One-liner to reset everything mid-session

If you've corrupted state and want a fresh start without leaving the
browser:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Mike.King\Workshop\Enki\scripts\start-dev.ps1 -Reset
```

Wait for the three host windows to settle, then hard-refresh
(Ctrl-F5) the browser. Sign back in.

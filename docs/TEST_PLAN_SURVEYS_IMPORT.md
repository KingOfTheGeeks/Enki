# Enki — Surveys & Import Test Plan

Step-by-step UI + API verification for the work shipped between
`85d30bb` and the latest commit:

- File-level survey import (CSV / TSV / whitespace / LAS)
- Bulk-clear surveys
- Inline tie-on edit as the first row of the surveys grid
- Tie-on overwrite conflict prompt (existing non-default tie-on protection)
- Auto-recalc on every survey / tie-on mutation (server-side)
- Depth-0-first-row → tie-on promotion in the importer
- Metric-only seed values, GUI conversion at the boundary
- **Three-tenant demo seed** — PERMIAN (Field, US unconventional +
  GoM exploration), NORTHSEA (Metric, UKCS + Wytch Farm onshore),
  BOREAL (Metric, Athabasca SAGD). Each tenant carries a primary
  Job demonstrating a distinct drilling-domain story plus, in some
  cases, a secondary Job add-on:
  - PERMIAN — `Crest-North-Pad` (8-well Wolfcamp pad) + `MC252-Relief`
    (Macondo-style relief-well intercept)
  - NORTHSEA — `Atlantic-26-7H` (Brent parallel-lateral pilot) +
    `Wytch-Farm-M-Series` (UK onshore ERD demo, ~10.7 km step-out)
  - BOREAL — `Cold-Lake-Pad-7` (SAGD producer + injector pair, the
    canonical SDI MagTraC ranging scenario)
- **Units display layer** wired through every Wells-area grid + form
  (headers, cells, edit templates, stat cards) — sections 13.1–13.9
- **Sidebar drill-in breadcrumb** — Job + Well below Jobs as you
  descend — section 14
- **CommonMeasure** treated as a dimensionless signal-calc multiplier
  (≈ 1.0), no longer mud weight — section 15
- **Wells trajectory plot** — multi-well overlay + single-well plot
  with Plan view + Vertical section tabs — section 16
- **Per-well magnetic reference** — Dec / Dip / Total field stored
  on the well, seeded with per-region WMM-2026 values — section 17
- **Travelling-cylinder anti-collision view** — third tab on the
  Wells trajectory plot; closest-approach distance + clock position
  from one target well to every sibling, vs target MD — section 18
- **Macondo-style relief-well showcase** — second Job under
  PERMIAN demonstrating anti-collision-in-reverse: twin reliefs
  converging from offset surface sites onto a near-vertical
  runaway via S-shape low-angle intercept trajectories — section 19
- **8-well Wolfcamp pad** (PERMIAN primary) — `Crest-North-Pad`,
  real-density anti-collision pressure on a single drilling pad
  — section 20
- **Wytch Farm M-series ERD** (NORTHSEA add-on) — single onshore
  pad, ~10.7 km lateral step-out under the English Channel —
  section 21
- **SAGD producer / injector pair** (BOREAL primary) —
  `Cold-Lake-Pad-7`, 5 m vertical-separation setpoint over ~700 m
  of lateral, the canonical SDI MagTraC ranging scenario —
  section 22

It's intentionally written as a checklist a human tester (Gavin) can
run end-to-end with no extra context. **Tick boxes as you go**; if a
step fails, note (1) which step, (2) what you saw, (3) any error from
the WebApi or Blazor windows.

> ℹ Convention used below: `(Gavin)` means a step is best run signed
> in as Gavin to exercise the cross-tenant admin path; `(Mike)` means
> sign in as Mike. Most steps work as either user — the labels matter
> only when the test is specifically about access.

> 📸 **Screenshot capture convention.** Sections 18 – 22 ship a new
> trajectory + cylinder-plot stack whose math has not been
> independently verified — the trajectories were authored from
> domain hand-math, not lifted from operator-confidential survey
> tables. Each chart-bearing step in §18 – §22 carries a
> `📸 Capture:` checkbox naming the file the tester should save.
> Save all screenshots to a folder of your choice (e.g.
> `tests-screenshots/`); Mike will collect them and upload back to
> Claude for geometric review against the design spec in each
> section. **Capture the screenshot only after ticking all the
> verification checkboxes above it** — that way the screenshot is
> a record of what the tester saw, not a record of a chart in mid-
> render.

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
  Expected: **307 / 307 passed** (71 + 22 + 211 + 3).

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

## 16. Wells trajectory plot — plan view + vertical section

The `/wells/plot` page overlays every well in a Job on a SfChart;
two tabs (Plan view, Vertical section) toggle between projections.
Same component renders single-well at `/wells/{wellId}/plot`.

### 16.1 Reaching the plot

- [ ] Land on **PERMIAN** → `Crest-22-14H` → click **Wells**.
- [ ] In the page header click **Plan view**. Lands on
  `/tenants/PERMIAN/jobs/{guid}/wells/plot`.
- [ ] Sidebar drill-in breadcrumb shows `Crest-22-14H` under `Jobs`
  (no Well entry — multi-well plot is a Job-level page).

### 16.2 Plan view — multi-well

- [ ] Page H1 reads `Wells plan view`; subtitle reads
  `PERMIAN · Plan view (looking down) — Easting × Northing.`
- [ ] Three series in the legend at the bottom: `Lone Star 14H`
  (cyan), `Lone Star 14I` (blue), `Caprock Federal 7` (grey).
- [ ] Axes: **Northing (ft)** on Y, **Easting (ft)** on X (Field
  tenant). Both values around the seed's surface coords (≈1.5M
  Northing, ≈600K Easting).
- [ ] Lone Star 14H + 14I curves drop south of the surface
  (Northing decreasing) as the lateral runs.
- [ ] Caprock Federal 7 plots as a near-point (offset is vertical
  with sub-2° drift).

### 16.3 Vertical section view

- [ ] Click the **Vertical section** tab. The chart re-renders
  in place; the toolbar buttons stay.
- [ ] H1 now reads `Wells vertical section`; subtitle reads
  `PERMIAN · Vertical section (side elevation) — V-sect × TVD,
  depth increasing downward.`
- [ ] **TVD axis is inverted** — `0` at top, growing downward
  (~5,000 ft for Permian's lateral, ~9,000 ft for the Bakken
  offset).
- [ ] Lambert / Lone Star wells: drop straight down from
  V-sect 0 at TVD 0, build to lateral, then horizontal sweep
  out to several thousand ft V-sect at the lateral TVD.
- [ ] Footer note **"each well's vertical-section X axis is
  projected onto its OWN tie-on's VerticalSectionDirection"**
  appears (multi-well V-sect comparability caveat).
- [ ] **No values in the millions on the V-sect axis.** A
  drilling-engineer-meaningful V-sect is in the same order of
  magnitude as the lateral length (thousands of ft). Big numbers
  here would mean the cached `Survey.VerticalSection` is stale
  from before the Marduk fix — `start-dev.ps1 -Reset` regenerates.

### 16.4 Single-well plot

- [ ] From the multi-well plot, click `Back to wells` → click
  `Lone Star 14H` → on the well's detail page click **Plan view**.
- [ ] Lands on `/tenants/PERMIAN/jobs/{guid}/wells/14/plot`
  (whatever the well's int id is).
- [ ] Sidebar drill-in shows `Crest-22-14H` AND `Lone Star 14H`
  (well is now in scope).
- [ ] H1 reads `Lone Star 14H — plan view`. Only one curve in
  the legend.
- [ ] On the Vertical section tab, the multi-well "VSD-mismatch"
  caveat is **gone** (single well doesn't have that ambiguity).
- [ ] **See all wells** button in the header navigates back to
  the multi-well overlay.

### 16.5 Metric tenant axes flip

- [ ] Sign out / sign in as needed; navigate to **NORTHSEA** →
  `Atlantic-26-7H` → Wells → **Plan view**.
- [ ] Y axis: `Northing (m)`; X axis: `Easting (m)`. Title
  shows `(Metric)`.
- [ ] Vertical section tab: TVD and V-sect axes both labelled
  `(m)`.
- [ ] Switch back to PERMIAN — labels flip back to `(ft)`.

### 16.6 Dark-theme + chart fits the card

- [ ] Chart background matches the surrounding `enki-card`
  (no white box).
- [ ] Plot fills the card width — legend at the bottom, axis
  labels visible at the bottom + left.
- [ ] No horizontal scrollbar at typical 1920×1080 viewports.

### 16.7 Empty-data handling

(Optional — only run if you want to exercise the edge case.)

- [ ] Create a new well with no surveys / no tie-on. Navigate to
  its `/plot` route.
- [ ] Page shows a friendly empty-state card: "No surveys or
  tie-ons recorded yet…", **not** an empty chart or a crash.

---

## 17. Magnetic reference per well

Every well now carries an optional magnetic-reference triple
(Declination / Dip / Total field) — the values that were applied
upstream when survey azimuths were corrected. The data lives on
the existing `Magnetics` table; per-well rows have a non-null
`WellId`, legacy per-shot lookup rows (when those are wired) keep
`WellId` null and don't collide.

### 17.1 Seeded values per tenant

After `start-dev.ps1 -Reset`, every well under a Job carries the
same per-region triple. Approximate WMM-2026 values:

| Tenant | Dec | Dip | Total field |
|---|---|---|---|
| `PERMIAN` | 5° | 63° | 50,300 nT |
| `BAKKEN` | 9° | 73° | 57,500 nT |
| `NORTHSEA` | 0.5° | 73° | 50,500 nT |
| `CARNARVON` | 1° | −50° | 57,000 nT |

- [ ] Sign in, click into PERMIAN → `Crest-22-14H` → `Lone Star 14H`.
- [ ] Below the Surveys / Tubulars / Formations / Common Measures
  stat cards, a **Magnetic reference** section shows the three
  values in a label/value grid.
- [ ] Permian values: Declination ≈ `5.000°`, Dip ≈ `63.000°`,
  Total field ≈ `50,300 nT`.
- [ ] An "Updated" / "by …" row appears with the auto-stamp from
  the seeder.
- [ ] Switch to BAKKEN's Lambert 2H, NORTHSEA's Brent A-12, and
  CARNARVON's Gorgon 9H — each shows its own region's triple.
  Carnarvon's Dip is **negative** (−50.000°) because it's southern
  hemisphere.

### 17.2 Edit flow

- [ ] Click **Edit magnetic reference** under the values.
- [ ] Form pre-fills with the seeded values.
- [ ] Change Declination to `7.5`, Dip to `60`, Total field to
  `49000`. Click **Save**.
- [ ] Bounces back to Well detail; the values now read 7.500° /
  60.000° / 49,000 nT.
- [ ] The "Updated" timestamp is freshly stamped with the current
  signed-in user (`mike.king` for the dev seeder).

### 17.3 Set-from-empty flow

This requires a well with no magnetic reference — easiest is to
clear an existing one first, or create a fresh well via
`+ New well`.

- [ ] On a well with no reference, the section reads "No magnetic
  reference recorded yet for this well." with a **Set magnetic
  reference** primary button.
- [ ] Click the button → blank form.
- [ ] Fill in Declination `4`, Dip `62`, Total field `49500`. Save.
- [ ] Returns to Well detail showing the new values.

### 17.4 Clear flow (idempotent)

- [ ] On a well that has a reference, click **Edit magnetic reference**.
- [ ] In the Danger zone at the bottom, click **Clear magnetic
  reference**. Button label flips to "Click again to confirm clear".
- [ ] Click again. Page returns to Well detail showing the
  "No magnetic reference recorded yet" empty state.
- [ ] Re-enter MagneticsEdit (via "Set magnetic reference"). The
  Danger zone is **not present** when there's no row to clear.

### 17.5 SQL spot-check

```powershell
sqlcmd -S 10.1.7.50 -U sa -P '!@m@nAdm1n1str@t0r' -Q "USE Enki_PERMIAN_Active; SELECT WellId, BTotal, Dip, Declination FROM Magnetics ORDER BY WellId"
```

- [ ] Three rows (one per well in the seed), all with non-null
  `WellId`, all carrying the Permian triple (50,300 / 63 / 5).
- [ ] No rows with `WellId IS NULL` in a fresh dev seed (the
  per-shot lookup pool is unpopulated until shots are recorded).

### 17.6 Cross-tenant isolation

- [ ] Run the SQL above against `Enki_BAKKEN_Active`. Three rows
  with the Bakken triple (57,500 / 73 / 9), all WellIds in the
  Bakken tenant's range.
- [ ] No cross-tenant leakage — Permian values do not appear in
  Bakken's table.

---

## 18. Travelling-cylinder anti-collision view

The Wells trajectory plot now sports a third tab — **Travelling
cylinder** — alongside Plan view and Vertical section. It plots
closest-approach distance from one user-picked **target well** to
every other well in the same Job, sampled at every target station,
with target MD on the Y axis (inversed so depth runs downward).

Math owner: Marduk's `AMR.Core.Uncertainty.AntiCollisionScanner` —
deterministic centre-to-centre 3-D distance for v1; ISCWSA error
cones land later. Enki's WebApi rehydrates `SurveyStation`s from
the cached Northing / Easting / TVD on each well's persisted
Survey + TieOn rows, hands them to the scanner, and projects the
result onto the wire.

### 18.1 Activation + initial target pick

The post-restructure roster doesn't have a clean 3-well
parallel-lateral pilot under PERMIAN any more — that primary Job
is now an 8-well pad (covered in §20). The classic three-well
Target / Injection / Offset shape this section was originally
designed against now lives at **NORTHSEA → Atlantic-26-7H**, so
the §18 walks all use that as the canonical demo Job.

1. Sign in (Mike). Navigate **Tenants → Brent Atlantic →
   Atlantic-26-7H (Job) → Wells → Plot**.
2. Confirm three tabs: **Plan view**, **Vertical section**,
   **Travelling cylinder**.
3. Click **Travelling cylinder**. Confirm:
   - [ ] The target-well picker appears, defaulted to **Brent A-12
     (Target)** — the Target-typed well wins the auto-pick when
     the route doesn't pin a specific well.
   - [ ] The chart loads inside ~1 s.
   - [ ] Two curves on the chart: **Brent A-13 (Injection)** in
     muted blue, **Brent A-7 (Offset)** in dim grey.
   - [ ] X axis label is **Closest-approach distance (m)** —
     NORTHSEA is a Metric tenant.
   - [ ] Y axis label is **Target MD (m)**, with depth running
     downward (axis inversed).
4. Hover any point on the A-13 curve. Tooltip reads
   `Brent A-13: <dist> away, MD <md>` with values in metres.
   Expected distance ~15 m through the lateral (the parallel-
   lateral pair-spacing signature).
- [ ] 📸 **Capture**: `18-1-northsea-atlantic26-cylinder-target-A12.png`
  — full cylinder chart with both curves (A-13 + A-7) visible,
  default target.

### 18.2 Picker re-load

1. Change the target well in the picker → **Brent A-13**.
2. Confirm:
   - [ ] "Scanning…" hint flashes briefly next to the picker.
   - [ ] The chart re-renders with two curves (the other Target +
     the Offset; the Injection well is gone — it's the new target,
     so it's excluded from its own scan).
   - [ ] Chart title updates to "Travelling cylinder — Brent A-13
     (Metric)".
- [ ] 📸 **Capture**: `18-2-northsea-atlantic26-cylinder-target-A13.png`
  — chart after target picker switched to A-13.

### 18.3 Single-well route mode

1. From the wells list, click into **Brent A-12** → **Plot**.
2. Switch to **Travelling cylinder**.
3. Confirm:
   - [ ] Target picker is pre-set to **Brent A-12** (the route's
     `WellId` wins over the Target-type default).
   - [ ] Same two curves render. The user can still pick a
     different target via the dropdown.

### 18.4 Empty-job + no-offsets edge cases

1. Sign in. Pick a Job with **no offset wells** (or create one and
   add only a single Target well + tie-on).
2. Open Plot → **Travelling cylinder**.
3. Confirm:
   - [ ] Chart area shows the message "No offsets to compare
     against. The target well needs at least one sibling under
     this Job with a tie-on and / or surveys recorded." — not a
     blank chart, not an error.
- [ ] 📸 **Capture**: `18-4-cylinder-no-offsets-empty-state.png`
  — the empty-state card.

### 18.5 Cross-tenant + cross-job isolation

The post-restructure demo has only PERMIAN + BOREAL as the
non-NORTHSEA tenants, so cross-tenant isolation is checked by
switching to one of those.

1. Switch to **Permian Crest** → **Crest-North-Pad** (Job) →
   **Wells → Plot → Travelling cylinder**.
2. Confirm:
   - [ ] Target picker contains only **Crest North 1H** through
     **Crest North 8H** — none of the NORTHSEA Brent wells.
   - [ ] Curve names + colours match PERMIAN's wells. No bleed-in
     from any other Job or tenant.

### 18.6 Units flip (Field ↔ Metric)

The two demo tenants ship in different unit systems, so the same
travelling-cylinder chart should render with different axis labels
depending on which tenant you're on.

1. From PERMIAN (above), confirm:
   - [ ] Axis labels read **Closest-approach distance (ft)** +
     **Target MD (ft)** — Field-units tenant.
2. Switch back to **Brent Atlantic → Atlantic-26-7H → Travelling
   cylinder**.
3. Confirm:
   - [ ] Axis labels read **Closest-approach distance (m)** +
     **Target MD (m)** — Metric-units tenant.
   - [ ] Distances are in metres; values look reasonable
     (~15 m for A-13's flat-line, hundreds-of-m for A-7's growing
     curve).

### 18.7 SQL spot-check (the math actually happened)

The endpoint hits no new tables — there's nothing to inspect in
SQL beyond confirming the Survey + TieOn rows have populated
Northing / Easting / VerticalDepth. As a sanity check:

```sql
USE Enki_PERMIAN_Active;
SELECT TOP 5
    w.Name, s.Depth AS MD, s.Northing, s.Easting, s.VerticalDepth
FROM Wells w
JOIN Surveys s ON s.WellId = w.Id
ORDER BY w.Name, s.Depth;
```

- [ ] All four numeric columns are non-zero (after the seeder ran;
  if all zero, Marduk's auto-recalc didn't fire — bug).

---

## 19. Macondo-style relief-well showcase

PERMIAN now carries a **second Job** alongside `Crest-22-14H` —
`MC252-Relief`, a Gulf-of-Mexico-flavoured relief-well intercept
demo. Four wells:

| Well | Type | Role |
|---|---|---|
| `MC252 Macondo` | Target | The runaway — near-vertical to ~5 500 m TVD (~18 040 ft) |
| `Development Driller II` | Injection | Primary relief, surface 1 500 m **north** of runaway, S-shape approach driving south |
| `Development Driller III` | Injection | Backup relief, surface 1 500 m **east** of runaway, S-shape approach driving west |
| `Atlantis-7 Producer` | Offset | Far-away vertical producer 3 km west — reference curve, never converges |

The reliefs share an identical **S-shape** trajectory (vertical
2 200 m → build to 55° → hold tangent 600 m → drop to 5° →
near-vertical approach 1 200 m, total ~6 000 m MD), differing
only in approach azimuth (180° for DDII, 270° for DDIII). At TD
both reliefs sit on the runaway's vertical column with closest
approach <100 m at the deepest target station.

Math owner: Marduk's `AntiCollisionScanner` (the same one that
drives §18). The point of this Job is to **show the math working
in reverse** — anti-collision normally measures "stay away";
relief drilling measures "converge to zero." Same geometry; flipped
intent.

### 19.1 Job exists + lists alongside Crest-North-Pad

1. Sign in (Mike). Navigate **Tenants → Permian Crest → Jobs**.
2. Confirm:
   - [ ] Two Jobs in the list: `Crest-North-Pad` (primary,
     §20) and `MC252-Relief` (relief showcase, this section).
   - [ ] `MC252-Relief` Region reads `Gulf of Mexico — Mississippi
     Canyon (exploration)`.
   - [ ] `MC252-Relief` UnitSystem is `Field` (inherits PERMIAN's).

### 19.2 Wells under MC252-Relief

1. Click into `MC252-Relief` → **Wells**.
2. Confirm four rows:
   - [ ] `MC252 Macondo` (Target)
   - [ ] `Development Driller II` (Injection)
   - [ ] `Development Driller III` (Injection)
   - [ ] `Atlantis-7 Producer` (Offset)

### 19.3 Trajectory plots — Plan view

1. **Wells → Plot → Plan view**.
2. Confirm:
   - [ ] All four curves render. The runaway is a tiny dot at the
     centre (vertical → no plan-view extent). DDII curves in from
     the north; DDIII curves in from the east; Atlantis-7 sits 3 km
     to the west as a single dot.
   - [ ] Approximate symmetry: DDII's surface position is
     ~1 500 m north of runaway, DDIII's is ~1 500 m east — visible
     by eye from the axis labels.
- [ ] 📸 **Capture**: `19-3-permian-mc252relief-planview.png`
  — full plan view with all four wells and a visible
  star pattern (runaway centre, DDII north, DDIII east,
  Atlantis-7 west).

### 19.4 Trajectory plots — Vertical section

1. Switch to **Vertical section** tab.
2. Confirm:
   - [ ] DDII + DDIII show the textbook S-shape: straight down for
     ~2 200 m, build to 55° around 3 000 m, hold tangent (visible
     as a slight slope change), drop to ~5° around 4 800 m, then
     near-vertical to ~6 000 m MD.
   - [ ] The runaway is a clean vertical line.
   - [ ] Atlantis-7 is also near-vertical (mild eastern drift).
- [ ] 📸 **Capture**: `19-4-permian-mc252relief-vsect.png` —
  full vertical section showing the S-shape relief profiles.

### 19.5 Travelling cylinder — the moneyshot

1. Switch to **Travelling cylinder**. Default target = `MC252
   Macondo` (the only Target-typed well in the Job).
2. Confirm three curves on the chart:
   - [ ] **Development Driller II** (Injection, blue) — starts at
     ~5 000 ft (≈ 1 500 m) at MD 0, descends as the relief turns
     toward the runaway, **converges to <300 ft at the deepest
     target station**. Concave shape — the converge-to-zero
     signature.
   - [ ] **Development Driller III** (Injection, blue) — same
     shape as DDII (mirrored — same trajectory math, different
     approach azimuth).
   - [ ] **Atlantis-7 Producer** (Offset, grey) — stays roughly
     **flat at ~10 000 ft (≈ 3 000 m)** throughout. Never
     converges; provides the visual contrast that makes the
     relief curves' convergence pop.
- [ ] 📸 **Capture**: `19-5a-permian-mc252relief-cylinder-target-runaway.png`
  — cylinder with all three curves; target = MC252 Macondo
  (the runaway).
3. Pick `Development Driller II` as the target instead. Confirm:
   - [ ] DDII is now excluded (target excludes itself).
   - [ ] DDIII + Atlantis-7 + MC252 Macondo render. The Macondo
     curve mirrors DDII's earlier shape — same geometry, swapped
     reference frame.
- [ ] 📸 **Capture**: `19-5b-permian-mc252relief-cylinder-target-DDII.png`
  — cylinder with target = Development Driller II.

### 19.6 SQL spot-check — geometry sanity

```sql
USE Enki_PERMIAN_Active;
SELECT TOP 5
    w.Name, s.Depth AS MD, s.Inclination, s.Azimuth,
    s.Northing, s.Easting, s.VerticalDepth
FROM Jobs j
JOIN Wells w ON w.JobId = j.Id
JOIN Surveys s ON s.WellId = w.Id
WHERE j.Name = 'MC252-Relief'
  AND w.Name = 'Development Driller II'
ORDER BY s.Depth;
```

- [ ] First few rows show inclination 0° (vertical phase).
- [ ] Northing roughly constant + offset ~1 500 m from MC252's
  Northing (DDII is 1 500 m north of runaway at surface).
- [ ] Easting matches MC252's Easting (DDII shares its E coord
  with runaway — surface offset is purely north-south).

---

## 20. 8-well Wolfcamp pad (PERMIAN primary Job)

PERMIAN's primary Job is now `Crest-North-Pad` — an 8-well
unconventional pad in the Permian Wolfcamp. All eight surface holes
sit within ~10 m of each other on the pad; each well drills
straight down through surface casing, then kicks off below ~1 000 m
TVD and turns to its individual reservoir cell. Lateral azimuths
fan over a ~30° spread (the reservoir trend, ~south); landing
depths stack across two reservoir benches (Wolfcamp A ~1 200 m,
Wolfcamp B ~1 400 m).

| Well | Type | Lateral az | Landing TVD |
|---|---|---|---|
| Crest North 1H | Target | 175° | 1 200 m |
| Crest North 2H | Injection | 180° | 1 200 m |
| Crest North 3H | Injection | 185° | 1 200 m |
| Crest North 4H | Injection | 178° | 1 400 m |
| Crest North 5H | Injection | 182° | 1 400 m |
| Crest North 6H | Injection | 186° | 1 400 m |
| Crest North 7H | Injection | 180° | 1 300 m |
| Crest North 8H | Offset | 184° | 1 300 m |

The point of this Job is to **demonstrate real-density anti-collision
pressure** — every well shares a 10 m radius with seven siblings in
the shallow vertical section. On a real rig the directional driller
is constantly checking the cylinder plot here.

### 20.1 Job + wells render

1. **Permian Crest → Crest-North-Pad → Wells**.
2. Confirm:
   - [ ] Eight rows visible. Names `Crest North 1H` … `Crest North 8H`.
   - [ ] Types: 1H = Target, 8H = Offset, 2H–7H = Injection.

### 20.2 Plan view — fan pattern

1. **Wells → Plot → Plan view**.
2. Confirm:
   - [ ] All eight curves emerge from a tight surface cluster
     (within ~10 m).
   - [ ] Curves fan out heading roughly south, with visible
     azimuth spread between leftmost (1H @ 175°) and rightmost
     (3H @ 185°).
   - [ ] Lateral landing positions sit ~2 km south of the pad.
   - [ ] **NOT** a single diagonal line from origin to the pad
     (that's the symptom of the earlier non-monotonic-depth bug
     that should now be fixed; see DevTenantSeeder.cs comment in
     `SeedMultiWellPadWell`).
- [ ] 📸 **Capture**: `20-2-permian-crestnorthpad-planview-fan.png`
  — full plan view with the 8-well fan visible.

### 20.3 Travelling cylinder — anti-collision pressure

1. Switch to **Travelling cylinder**. Default target = `Crest North
   1H` (the Target-typed well).
2. Confirm:
   - [ ] **Seven sibling curves** on the chart.
   - [ ] In the shallow vertical section (target MD 0 → ~3 000 ft),
     **all seven curves are clustered close together at distance
     <30 ft** — this is the anti-collision pressure zone, where
     ranging tools earn their pay.
   - [ ] Past the kick-off (~3 000 ft), curves **diverge** as each
     well's lateral takes it to its own reservoir cell.
   - [ ] Curves landing at the same bench depth (e.g. 1H, 2H, 3H
     at 1 200 m) maintain similar separation profiles; curves to
     a different bench (4H–6H at 1 400 m) diverge further as
     they descend.
- [ ] 📸 **Capture**: `20-3-permian-crestnorthpad-cylinder-target-1H.png`
  — full cylinder with all 7 sibling curves; target = Crest
  North 1H.

### 20.4 SQL spot-check — pad surface tightness

```sql
USE Enki_PERMIAN_Active;
SELECT w.Name, t.Northing, t.Easting
FROM Jobs j
JOIN Wells w  ON w.JobId = j.Id
JOIN TieOns t ON t.WellId = w.Id
WHERE j.Name = 'Crest-North-Pad'
ORDER BY w.Name;
```

- [ ] All eight tie-ons sit within ~10 m of each other (max delta
  Northing or Easting between any pair < ~10 m). This is the
  "all wells share a pad" invariant.

---

## 21. Wytch Farm M-series ERD (NORTHSEA add-on Job)

NORTHSEA carries a **second Job** alongside `Atlantic-26-7H` —
`Wytch-Farm-M-Series`, BP's UK onshore ERD pad in Dorset. Two
wells (M-11 + M-16) drilled from a single onshore pad, ~10.7 km
laterally southeast under Poole Bay to the Sherwood reservoir.

The point: **the geometric extreme**. Plan view axes stretch to
10 km+ — a stress test for the rendering side. Vertical section
shows the textbook ERD profile (short build, very long tangent at
~87°).

**Realism note**: gross trajectory parameters (10.7 km step-out,
1.6 km TVD, build profile) are public via BP / OGA papers. Exact
survey rows are operator-confidential, so this trajectory is
shape-accurate, not row-accurate.

### 21.1 Job exists alongside Atlantic-26-7H

1. **Brent Atlantic → Jobs**.
2. Confirm:
   - [ ] Two Jobs: `Atlantic-26-7H` (existing) + `Wytch-Farm-M-Series`
     (new).
   - [ ] `Wytch-Farm-M-Series` Region reads
     `UK Onshore — Wytch Farm (Dorset)`.

### 21.2 Plan view — the long arrow

1. **Wytch-Farm-M-Series → Wells → Plot → Plan view**.
2. Confirm:
   - [ ] Both wells (M-11 + M-16) render as long curves heading
     **south-east** from a tight surface pad.
   - [ ] X-axis (Easting) and Y-axis (Northing) both stretch to
     **6 000 m+ delta** — much wider than any other Job in the demo.
   - [ ] M-11 and M-16 run roughly parallel, ~50 m apart in plan view.
- [ ] 📸 **Capture**: `21-2-northsea-wytchfarm-planview-erd.png`
  — full plan view; chart axes should clearly show the 10 km+
  step-out arrow.

### 21.3 Vertical section — the ERD profile

1. Switch to **Vertical section**.
2. Confirm:
   - [ ] Both curves: short build phase (~1 000 → 1 800 m MD),
     then **very long shallow-sloping tangent** at ~87° inclination
     extending out past 9 000 m vertical-section.
   - [ ] TVD stays near 1 600–2 100 m for the entire lateral —
     classic ERD profile.
- [ ] 📸 **Capture**: `21-3-northsea-wytchfarm-vsect-erd.png`
  — full vertical section showing the long-tangent ERD profile.

### 21.4 Travelling cylinder — parallel-lateral signature

1. Switch to **Travelling cylinder**. Target = `M-11`.
2. Confirm:
   - [ ] Single sibling curve (M-16, blue).
   - [ ] Distance starts at ~50 m at surface (the pad offset),
     stays roughly constant through the build, **stays close to
     50 m for the entire ~10 km lateral** — the parallel-lateral
     signature, just over a much longer distance than any other
     Job in the demo.
- [ ] 📸 **Capture**: `21-4-northsea-wytchfarm-cylinder-target-M11.png`
  — full cylinder; M-16 curve should be a near-flat ~50 m line
  out to MD ~11 400 m.

### 21.5 SQL spot-check — step-out distance

```sql
USE Enki_NORTHSEA_Active;
SELECT TOP 1
    w.Name, MAX(s.Depth) AS MaxMd,
    MAX(s.Northing) - MIN(s.Northing) AS NorthingSpan,
    MAX(s.Easting)  - MIN(s.Easting)  AS EastingSpan
FROM Jobs j
JOIN Wells w   ON w.JobId = j.Id
JOIN Surveys s ON s.WellId = w.Id
WHERE j.Name = 'Wytch-Farm-M-Series' AND w.Name = 'M-11'
GROUP BY w.Name;
```

- [ ] `MaxMd` ≈ 11 400 m.
- [ ] Combined Northing + Easting span ≈ 10 km+ (the step-out).

---

## 22. SAGD producer / injector pair (BOREAL primary Job)

BOREAL is a new tenant — Athabasca / Cold Lake bitumen operator,
Metric units. Primary Job `Cold-Lake-Pad-7` is the canonical
**Steam-Assisted Gravity Drainage** showcase: a horizontal
**producer** at the bottom of the McMurray pay zone (~470 m TVD)
plus a horizontal **injector** 5 m directly above it (~465 m TVD).
Both ~700 m of lateral.

Two wells only — earlier iteration carried a legacy CHOPS
vertical reference too, but its growing distance as the SAGD
pair drilled east dominated the cylinder x-axis and visually
compressed the 5 m setpoint into a pinned-to-zero line.
Dropping CHOPS lets the chart auto-scale so the 5 m signature
reads cleanly.

The 5 m vertical separation is **the whole game** — too close =
thermal short-circuit, too far = no gravity drainage. Holding the
pair to ±0.5 m of the 5 m setpoint over ~700 m of lateral is
exactly what passive magnetic ranging (SDI's MagTraC) is for.

This Job demonstrates a **third use case** for the anti-collision
math beyond §18 ("stay X away") and §19 ("converge to zero"):
**tracking a setpoint**.

### 22.1 BOREAL tenant + Job exists

1. Sign in. Navigate **Tenants**.
2. Confirm:
   - [ ] Three tenants visible: Permian Crest, Brent Atlantic,
     **Boreal**.
   - [ ] **Boreal → Jobs** lists `Cold-Lake-Pad-7`.
3. Click into the Job. Confirm:
   - [ ] Region reads `Athabasca — Cold Lake`.
   - [ ] UnitSystem is `Metric` (axes label in metres).

### 22.2 Wells under Cold-Lake-Pad-7

1. **Cold-Lake-Pad-7 → Wells**.
2. Confirm two rows:
   - [ ] `Cold Lake Pad-7 P1` (Target — the producer, lower)
   - [ ] `Cold Lake Pad-7 I1` (Injection — the injector, upper)

### 22.3 Vertical section — the SAGD shape

1. **Wells → Plot → Vertical section**.
2. Confirm:
   - [ ] P1 + I1 both kick off around MD ~340 m, build to 90° by
     MD ~540 m, then run horizontal east to ~1 240 m MD.
   - [ ] P1 lateral sits at TVD ~470 m (the McMurray pay zone).
   - [ ] **I1's lateral sits ~5 m shallower than P1's** (TVD
     ~465 m). Will look near-overlapping at chart resolution
     — zoom in on the lateral section if you want to see the
     5 m gap explicitly.
- [ ] 📸 **Capture**: `22-3-boreal-coldlakepad7-vsect-sagdpair.png`
  — full vertical section. Both wells in the McMurray pay
  zone (~470 m TVD); the 5 m gap will be visually subtle but
  the J-curve into 90° lateral should be clean.

### 22.4 Travelling cylinder — the moneyshot (5 m setpoint)

This is the Job's headline reading.

1. Switch to **Travelling cylinder**. Target = `Cold Lake Pad-7 P1`.
2. Confirm:
   - [ ] **One sibling curve**: `Cold Lake Pad-7 I1` (Injection,
     blue).
   - [ ] X-axis auto-scales to ~10 m or less (no long-range
     reference well dragging the chart out — that's the design
     decision).
   - [ ] **I1's curve sits at ~5 m through the entire lateral**
     (target MD ~540 → 1 240 m). This is the setpoint-tracking
     signature — the picture SDI's MagTraC ranging tool was
     designed to produce. The shape is **flat** at 5 m, not a
     converge-to-zero or growing-distance curve.
   - [ ] In the vertical phase + build phase (target MD 0 → 540 m),
     distance is small but non-flat — the wells are at the same
     N/E surface position but have slightly different KOP depths,
     so distance varies as the build phases offset.
- [ ] 📸 **Capture**: `22-4-boreal-coldlakepad7-cylinder-target-P1.png`
  — full cylinder showing the flat 5 m injector signature.
  This is the SDI MagTraC headline shot.

### 22.5 SQL spot-check — the 5 m separation

```sql
USE Enki_BOREAL_Active;
SELECT
    w.Name, s.Depth AS Md, s.VerticalDepth AS Tvd,
    s.Northing, s.Easting
FROM Jobs j
JOIN Wells w   ON w.JobId = j.Id
JOIN Surveys s ON s.WellId = w.Id
WHERE j.Name = 'Cold-Lake-Pad-7'
  AND s.Depth > 600
ORDER BY w.Name, s.Depth;
```

- [ ] At lateral MD (>600 m), P1 + I1 stations have nearly identical
  Northing **and** Easting (within ~1 m) — both wells share the
  same surface coords and same lateral azimuth.
- [ ] At matching lateral MD, **P1's TVD is ~5 m greater than I1's TVD**
  — confirms the 5 m **vertical** separation (the SAGD-correct
  geometry, not the earlier broken horizontal-only offset).

---

## 23. Reporting

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

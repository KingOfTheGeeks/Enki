# Plan — Units Display Layer

**Goal:** every numeric value in the UI renders in the unit system the
Job is configured for; every input form accepts the user's preferred
unit and converts to SI before save. The DB never sees anything but SI.

**Status today:** the DB stores SI per the locked-in rule. The
Wells-area grids and forms render the raw stored values with no unit
suffix. The North Sea seed tenant defaults to `UnitSystem.Metric` and
the two onshore tenants to `UnitSystem.Field` — but that tenant
preference is invisible because the UI doesn't honor it.

**Why now:** infrastructure is already built and unit-tested
(`Measurement`, `UnitConverter`, `UnitSystemPresets`, `EnkiQuantity`).
We've deferred the UI wiring twice. The list of pages it touches only
grows. With three tenants now spanning two unit systems, the gap is
visibly wrong.

---

## 1. What's already there

```
SDI.Enki.Core/Units/
  EnkiQuantity.cs            ← physical-quantity enum (Length, Density, …)
  UnitSystem.cs              ← SmartEnum (Field / Metric / SI / Custom)
  UnitSystemPresets.cs       ← (system, quantity) → (UnitsNet enum, abbrev)
  UnitConverter.cs           ← SI ↔ display-unit math, UnitsNet bridge
  Measurement.cs             ← (SiValue, Quantity) struct + .Format(system)
```

`Measurement.Format(system, "F2")` already returns `"1,000.00 ft"` for
a Length stored as 304.8 m and rendered under `UnitSystem.Field`.
`MeasurementTests` confirms the round-trip. The Format pipeline is
the foundation; the GUI work is wiring it into Razor.

## 2. Architectural decisions — locked in for this plan

These are calls I'm making upfront so the plan reads concretely. Push
back on any of them in the open-questions section before I start coding.

### 2.1 Where does the Job's `UnitSystem` come from?

**Cascading parameter from a `JobUnitContext` component.** Each page
under `/jobs/{jobId}/...` wraps its content in a `JobUnitContext`
that fetches `Job.UnitSystem` once on init and exposes it via
`[CascadingParameter]` to every descendant. Children that need the
unit system declare:

```csharp
[CascadingParameter] public UnitSystem Units { get; set; } = UnitSystem.SI;
```

Alternatives rejected:
- A scoped service that reads the current route — implicit, hard to
  test, doesn't compose with multi-tenant SignalR circuits.
- Refetching per-component — cache misses on every grid render.

### 2.2 Angle storage — degrees in DB, but "SI is radians"

**Special-case angles: skip unit conversion, render with the `°`
suffix universally.** Surveys / tie-ons store inclination + azimuth as
**degrees**, despite `UnitSystemPresets` declaring `SI: radians`.
That's a de-facto storage convention from Marduk's `MinimumCurvature`
which works in degrees throughout. Migrating storage to radians is a
data + math correctness change with no user-visible benefit (every
oilfield user reads angles in degrees regardless of unit system).

The `<UnitFormatted>` component will short-circuit on `EnkiQuantity.Angle`
and render the raw double + `°` suffix, no conversion. Same for
`<UnitInput>` — angle inputs accept degrees, store degrees.

This leaves the SI-strict preset's `rad` row in `UnitSystemPresets` as
a theoretical entry that's never hit through the UI. Document and
move on.

### 2.3 Quantities that need per-column overrides

| Column | Stored | Display per Job system |
|---|---|---|
| Tubular `Diameter` | meters | **`in` for Field, `mm` for Metric / SI** |
| Tubular `Weight` | kg/m | `lb/ft` for Field, `kg/m` for Metric / SI |

`<UnitFormatted>` accepts an optional `OverrideUnit` parameter (a
UnitsNet enum) so a specific column can pick a unit that doesn't
match the Job's preset entry. The tubular OD uses the override on
Field (`LengthUnit.Inch`) and Metric / SI (`LengthUnit.Millimeter`)
since neither system's default `Length` unit (ft / m) is right for a
13⅜″ casing string.

### 2.4 DLS (rate per averaging-window)

DLS is stored as **degrees per 30 m** (Marduk's default
`metersToCalculateDegreesOver`). Display per Job system:

| System | Display unit | Conversion from stored |
|---|---|---|
| Field | `°/100ft` | × `30.48 / 30 = 1.016` |
| Metric, SI | `°/30m` | identity |

The conversion is mechanical (one constant per system); fold it into
`UnitConverter.ConvertOilfield` alongside SonicTransitTime, since
DLS — like sonic — has no UnitsNet quantity. Add a new `EnkiQuantity`
value (`DoglegSeverity = 70`) so we can hang the per-system display
unit off `UnitSystemPresets`.

### 2.5 Build / Turn (per-station)

Per Mike: build and turn are conventionally **degrees per station**
(raw inclination / azimuth delta between adjacent rows). Marduk's
output is normalized to the same 30 m window as DLS.

For the display layer: render Marduk's value with a `°/30m` (Metric /
SI) or `°/100ft` (Field) label — same treatment as DLS — and flag the
"true per-station" rendering as a **separate calculation-storage
change** outside this plan's scope. Rationale: changing what the auto-
calc stores affects how every existing survey's Build / Turn columns
read against historical data; that's a domain decision, not a UI one.

If you'd rather see real per-station deltas in the next ship, the
follow-on is a one-pager: change `MardukSurveyAutoCalculator` to
write `curr.Inc - prev.Inc` and the wrapped azimuth delta directly,
drop the `metersToCalculateDegreesOver` scaling. Same DTO column
names, different stored values.

### 2.5 Coordinates — Northing / Easting

**Convert via `EnkiQuantity.Length`** (so Field jobs show feet,
Metric jobs show meters). State-plane systems are sometimes ft and
sometimes m in real life; for the seed data we own, the values are
stored in meters per the rule and the Field preset converts them on
display. Consistent with depth.

### 2.6 New `EnkiQuantity` value: `LinearMassDensity`

For tubular weight (`lb/ft` ↔ `kg/m`). One new entry on the enum,
one new switch arm in `UnitConverter` (UnitsNet has
`LinearDensityUnit`), one row each in the three presets. Small
addition, opens the door cleanly.

---

## 3. Components

### 3.1 `<UnitFormatted>` — render

```razor
<UnitFormatted Value="@row.Depth"
               Quantity="EnkiQuantity.Length"
               Format="F2" />
```

**Parameters:**
- `Value` (`double`) — the SI-stored value
- `Quantity` (`EnkiQuantity`) — what kind of quantity
- `Format` (`string?`) — number-format spec (default `"N"`)
- `ShowUnit` (`bool` = `true`) — whether to append the abbreviation
- `OverrideUnit` (`Enum?`) — opt out of the Job's preset for this cell

**Render:**
- For `EnkiQuantity.Angle`: raw double + `°` (no conversion)
- For oilfield-only quantities (GammaRay, Porosity, etc.):
  pass-through with the static abbreviation
- Otherwise: route through `Measurement.From(value, quantity).Format(units, format)`

Pulls `[CascadingParameter] UnitSystem Units` from `JobUnitContext`.

### 3.2 `<UnitInput>` — input

```razor
<UnitInput @bind-SiValue="@form.Depth"
           Quantity="EnkiQuantity.Length"
           Step="0.01" />
```

**Parameters:**
- `SiValue` (`double`, two-way) — bound to the form's SI value
- `Quantity` (`EnkiQuantity`) — drives unit selection
- `Step` (`double` = `0.01`) — number-input step
- `OverrideUnit` (`Enum?`) — same override hook as `<UnitFormatted>`

**Behavior:**
- Renders an `<input type="number">` whose visible value is the
  current `SiValue` projected into the Job's preferred unit.
- On user input: read display-unit value, convert to SI via
  `UnitConverter.ToSi`, update the bound `SiValue`.
- For `EnkiQuantity.Angle`: pass-through (degrees in / degrees out).
- Optional `<span class="enki-mono">@unitAbbreviation</span>` next to
  the input — small, low-contrast, just so the user knows the unit.

### 3.3 `<JobUnitContext>` — context provider

```razor
<JobUnitContext TenantCode="@TenantCode" JobId="@JobId">
    @* page content *@
</JobUnitContext>
```

**Behavior:**
- On init, fetches the Job (`GET /tenants/{c}/jobs/{j}`) and reads
  `UnitSystem`.
- Wraps children in `<CascadingValue Value="@_unitSystem">`.
- While loading, renders a small spinner (or just defers the
  CascadingValue → children see the default `UnitSystem.SI` and show
  raw SI numbers for half a second; acceptable trade).
- Exposes `[CascadingParameter] UnitSystem Units` and a
  `[CascadingParameter] Job? CurrentJob` for components that need the
  whole record.

Implementation note: it's tempting to use the master-DB `Job` entity
directly, but that would couple the BlazorServer to Infrastructure.
Stick to the existing `JobDetailDto` from `SDI.Enki.Shared.Jobs`.

---

## 4. Phased delivery

Each phase ships independently, builds clean, tests pass.

### Phase A — Foundations (~1 hour)

- Add `EnkiQuantity.LinearMassDensity = 60`
- Add corresponding rows in `UnitSystemPresets` (Field: `lb/ft`,
  Metric / SI: `kg/m`) and the `UnitConverter` switch arms (UnitsNet
  has `LinearDensity`)
- Build `<JobUnitContext>` component
- Build `<UnitFormatted>` component
- Build `<UnitInput>` component
- Unit tests on each — hit Field / Metric / SI / Angle / overrides

### Phase B — Surveys grid + tie-on inline edit (~2 hours)

- Wrap `Surveys.razor` content in `<JobUnitContext>`
- Replace each `GridColumn` numeric Format with a Template that uses
  `<UnitFormatted>` — Depth, TVD, Sub-sea, North, East, Northing,
  Easting (Length); DLS, Build, Turn (raw); Inc, Az (Angle)
- Update column headers to include the resolved abbreviation
  (`Depth (ft)` / `Depth (m)`)
- Replace the Syncfusion edit cells with `<UnitInput>`-based
  EditTemplates so the user types in Job units; conversion happens
  on `OnActionComplete` before the PUT
- Verify against both Permian (Field) and North Sea (Metric) seed
  tenants — same DB values, different rendered displays

### Phase C — Survey single-row edit + create (~1 hour)

- `SurveyEdit.razor` form fields → `<UnitInput>`
- Read-only computed columns → `<UnitFormatted>`
- `NewSurvey.razor` → `<UnitInput>` for the three observed values

### Phase D — Tubulars (~1.5 hours)

- Wrap `Tubulars.razor` page in `<JobUnitContext>`
- `FromMeasured` / `ToMeasured` use Length per Job preset
- `Diameter` uses **Inch override** (always inches, regardless of preset)
- `Weight` uses `LinearMassDensity` per Job preset
  (`lb/ft` for Field, `kg/m` otherwise)
- Edit + create forms use `<UnitInput>` with the same overrides

### Phase E — Formations (~45 min)

- `Formations.razor` grid + edit + create
- `FromVertical` / `ToVertical` are Length per Job preset
- `Resistance` is `Resistivity` (`Ω·m` everywhere)

### Phase F — CommonMeasures (~45 min)

- `CommonMeasures.razor` grid + edit + create
- `FromVertical` / `ToVertical` are Length per Job preset
- `Value` is a dimensionless signal-calculation scaling multiplier
  (a "fudge factor", typically 0.85–1.15 with 1.0 meaning no
  adjustment). Render as a bare double — no `EnkiQuantity` tag, no
  unit projection.

### Phase G — Wells / WellDetail / WellEdit (~30 min)

Mostly title bars and stat cards. No quantity-bearing columns to
convert; just headers like `Stations` / `Min depth` should pick up
the depth abbreviation.

### Phase H — Importer status messages (~15 min)

`SurveyImportButton` shows imports as e.g.
`"Imported 10 stations from Csv (detected ft)"`. Already in place;
just align the abbreviation source on `UnitSystemPresets` so it
matches the rest of the UI.

### Phase I — Test plan + targeted unit tests (~1 hour)

- Add a section to `TEST_PLAN_SURVEYS_IMPORT.md` that walks Permian
  vs North Sea tenants and verifies:
  - Same DB Northing for the Bakken target (5 300 000 m) renders
    `~5 300 000.00 m` in Bakken (Metric? wait — Bakken is Field) —
    let me re-check…

  *(Bakken is `UnitSystem.Field` in the seed, so its Northing renders
  in feet: 5 300 000 m × 3.28084 ≈ 17 388 451 ft. North Sea is Metric
  so its 6 700 000 m renders as `6 700 000 m`.)*

  - First survey depth on Lone Star 14H reads `1000 ft` (was
    `304.80 m`)
  - First survey depth on Brent A-12 reads `304.8 m` (unchanged
    numerically, just labeled)
- Component-level Razor tests aren't needed if the underlying
  `Measurement` round-trips already cover the math; just smoke-test
  rendering.

**Total budget:** ~9 hours of focused work, shippable per phase.

---

## 5. Edge cases worth flagging up front

| # | Case | Handling |
|---|---|---|
| 1 | Job's `UnitSystem` not yet loaded on first paint | Default the cascading value to `SI` — half-second flash of raw SI numbers, then re-render. Acceptable; alternative is a full-page loading gate. |
| 2 | Stat cards `Min depth` / `Max depth` aggregate across the grid | Compute the aggregate in SI, render the aggregate via `<UnitFormatted>`. Exactly what the column-cell render does; no special case. |
| 3 | Survey importer ingesting a file in feet for a Metric Job | Already works — importer converts to SI on the way in; display layer converts back to whichever unit the Job prefers. The two are independent. |
| 4 | User edits a depth in Field-units (e.g. types `1000.00`); SI value becomes `304.8 m` | `<UnitInput>` converts on the way out. Round-trip stable for clean factors (ft↔m is exact). Watch out for printed precision — `0.3048` × 1000 = `304.8` exact, but `999.99` × 0.3048 = `304.79696...` — show the user as many decimals as they typed, but SI carries the full double. |
| 5 | Switching Job's UnitSystem mid-session | Today: `UnitSystem` is set on Job creation and not exposed in the UI for edit. If we add an editor later, force-reload pages on change so cached cascading values rebuild. |
| 6 | Non-tenant pages (Tenants list, Admin) | No Job context → no unit conversion. Fall back to raw display. Already the case today. |
| 7 | Multi-tenant cross-page comparisons | Each well's page has its own JobUnitContext. Comparing two wells under different unit systems means two pages or two browser tabs. No special handling needed. |
| 8 | Wire-format DTOs | Stay SI. The conversion is purely a display + input concern; the API contract doesn't change. Future external clients still see `"depth": 304.8` and decide their own display strategy. |

---

## 6. Migration / data correctness — out of scope here

Two things this plan deliberately doesn't fix; we accept the existing
state:

1. **Angles stored as degrees** when the units doc says SI is radians.
   The display layer short-circuits angles. A future migration could
   move storage to radians, but the user-visible behavior stays
   identical so it's pure cleanup.

2. **Tubular dimensions stored in meters but typically displayed in
   inches even in metric-country jobs.** The override flag covers
   today's wiring; if a customer asks for cm-OD displays we add a
   second override case.

---

## 7. Resolved questions (locked-in answers)

1. **Cascading-parameter approach** — ✓ approved.
2. **Tubular OD** — ✓ `in` for Field, `mm` for Metric / SI.
3. **DLS** — ✓ rate per averaging-window: °/100ft for Field,
   °/30m for Metric / SI.
4. **Build / Turn** — semantically per-station (raw delta) per Mike.
   Display as DLS-shaped for now; the underlying calculation switch
   to true per-station deltas is a separate calc change.
5. **Surveys / radians** — ✓ user never sees radians. Angles
   short-circuit through the unit conversion and always render with
   the `°` suffix.
6. **All distances convert** — ✓ Northing / Easting / TVD / Depth /
   etc. all convert via `EnkiQuantity.Length` per the Job's preset.
7. **Phase order** — Surveys first (most-visited page; biggest
   user-visible payoff).
8. **Angle storage migration** — punt indefinitely; the display
   layer hides the SI-as-radians theoretical from the user.

---

## 8. What this plan does NOT cover

- Sorting / filtering by display value (we don't allow either right
  now on Surveys, so it's moot)
- Localised number formats (`1.000,00` for `de-DE`) — `Format` uses
  invariant; revisit if a customer asks
- Currency or any non-physical quantity
- Custom per-user unit overrides on top of a base preset (the
  `UnitSystem.Custom` placeholder is reserved for that and not in
  this plan's scope)
- Updating any of the AMR.Core.* libraries — they stay SI-only; the
  conversion lives entirely in the BlazorServer + tests

---

## 9. Acceptance — what "done" looks like

- A Permian-tenant survey row reads `1000.00 ft` for the first
  station's depth where it currently reads `304.80`.
- The same row on the North Sea tenant (Brent A-12) reads
  `304.80 m` — same DB value, different presentation.
- Editing the tie-on row's Depth on a Permian-tenant well, typing
  `1100`, hitting Update, results in a DB column value of
  `335.28 m` and a re-rendered grid showing `1100.00 ft`.
- Tubulars page on a Permian tenant shows OD `13.375 in`, weight
  `68 lb/ft` for the surface casing; same well on a Metric tenant
  shows OD `13.375 in` (override), weight `101.20 kg/m`.
- All 223 / 223 existing tests still pass.
- Build clean, 0 warnings.
- Test plan section walks the cross-tenant verification.

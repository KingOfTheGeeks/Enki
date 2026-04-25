Survey-import sample files for Enki's "Import file" button on
the Surveys page (/tenants/{code}/jobs/{jobId}/wells/{wellId}/surveys).

Pick any of these from the file dialog. The importer detects the
format and depth unit automatically; the page reloads after upload
with the auto-calculated trajectory.

  01-vertical-well-metric.csv     Simple metric CSV, near-vertical pilot
                                  to 1500 m. Quickest sanity check.

  02-horizontal-lateral-feet.csv  Header units in (ft) — proves the
                                  feet-to-meters conversion fires.
                                  Vertical → 90° lateral to MD 10 000 ft
                                  (3 048 m); end TVD ~ 1 700–1 800 m.

  03-compass-export-with-metadata Comment-style metadata harvested
                                  (well name, depth unit). Carries
                                  pre-computed TVD/North/East/DLS that
                                  ride along on each station for audit
                                  but are NOT trusted — Marduk re-runs
                                  min-curvature.

  04-survey-las2.las              LAS 2.0 with ~V/~W/~C/~P/~O/~A
                                  sections. Well name "BAKER 5H" pulls
                                  through from the ~Well section. Build
                                  to 90° at azimuth 135° (NE-bound).

  05-tab-delimited.tsv            TSV — same shape as CSV, different
                                  delimiter; format detector picks tab.

  06-whitespace-fixed-columns.txt Whitespace-aligned columns; detector
                                  picks runs-of-whitespace.

What each file produces in the grid:
  * Every file's depth-0 first row gets promoted to the tie-on by
    the importer (TIEON_FROM_FIRST_ROW note surfaces in the import
    notes panel). The tie-on row appears at the top of the grid;
    survey stations follow ordered by depth.
  * If the well already has a tie-on with non-default values, the
    server returns 409 and the import button shows an "Overwrite vs
    Keep existing" prompt before committing. The all-zero seed
    tie-on is treated as a default and silently overwritten.
  * Computed columns (TVD, Sub-sea, North, East, DLS, V-sect,
    Northing, Easting, Build, Turn) populate from the auto-calc that
    fires server-side as soon as the rows land — no manual calculate
    step.

The Import notes panel below the button lists any warnings the
parser emitted — defaulted units, normalised azimuths (e.g. -10° →
350°), dropped NaN rows, the tie-on promotion. Codes are stable
strings (UNIT_DEFAULT_USED, AZIMUTH_NORMALISED, ROWS_PRE_SORTED,
TIEON_FROM_FIRST_ROW, …) so they're filterable later.

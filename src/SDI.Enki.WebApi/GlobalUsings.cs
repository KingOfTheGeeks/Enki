// Policy-name constants moved to SDI.Enki.Shared.Authorization in the
// authorization redesign so the BlazorServer host can reference the
// same EnkiPolicies.* literals at [Authorize(Policy = ...)] sites
// without duplicating the strings. Global-using here means every
// controller's existing `EnkiPolicies.CanFoo` reference resolves
// without a per-file `using` line — kept the controller diffs small.
global using SDI.Enki.Shared.Authorization;

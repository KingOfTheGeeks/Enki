using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;
using AMR.Core.Survey.Extensions;
using AMR.Core.Survey.Services;

using MardukSurveyStation = AMR.Core.Survey.Models.SurveyStation;
using MardukTieOn = AMR.Core.Survey.Models.TieOn;

namespace SDI.Enki.Infrastructure.Surveys;

/// <summary>
/// Resolves true vertical depth at an arbitrary measured depth on a
/// well by delegating to Marduk's
/// <see cref="ISurveyInterpolator.InterpolateDepth"/> — the same
/// minimum-curvature math that produced the per-station <c>VerticalDepth</c>
/// values written by <c>MardukSurveyAutoCalculator</c>. Using the same
/// interpolator means a derived Formation/CommonMeasure/Tubular TVD
/// is consistent with the Survey grid the user is reading on screen.
///
/// <para>
/// Enki <i>never</i> reimplements the math: callers pass a Well's
/// existing TieOn + Survey rows in (already carrying populated
/// trajectory columns) and Marduk hands back a fully-resolved
/// <see cref="MardukSurveyStation"/> per target MD. The
/// <see cref="ResolveAsync"/> overload is a convenience that loads
/// the well's stations and resolves a single
/// <c>(fromMd, toMd)</c> pair; controllers that resolve many entities
/// per request use the eager <see cref="LoadStationsAsync"/> +
/// <see cref="ResolvePair"/> pair so the station list is fetched once.
/// </para>
///
/// <para>
/// <b>Tie-on as station[0].</b> The well's TieOn is converted to a
/// SurveyStation via Marduk's <c>TieOn.ToSurveyStation()</c> extension
/// and prepended to the survey list — same shape the auto-calc uses.
/// This lets interpolation work for MDs at or below the first survey
/// row (a Tubular at MD = 0 needs station[0] = tie-on as the
/// "previous"). The Survey rows themselves carry pre-computed
/// minimum-curvature columns from <c>MardukSurveyAutoCalculator</c>;
/// <c>SetMinimumCurvature</c> rehydrates them so the interpolator can
/// read previous.VerticalDepth / North / East when bridging stations.
/// </para>
///
/// <para>
/// <b>Range contract.</b> The interpolator throws if a target MD is
/// outside the bracketing pair (or fewer than two stations exist).
/// Controllers gate on
/// <c>ValidateAgainstSurveyRangeAsync</c> before write paths so the
/// 400 / 409 surfaces from the controller, not as a 500 from
/// somewhere deep in the calc layer. Read paths swallow
/// <see cref="ArgumentOutOfRangeException"/> /
/// <see cref="ArgumentException"/> and project null TVDs — a row
/// might have been written when the well had a wider survey set, and
/// we don't want a single out-of-range row to 500 the whole list.
/// </para>
///
/// <para>
/// Stateless / no captured DI — safe as a singleton.
/// </para>
/// </summary>
public sealed class SurveyTvdResolver(ISurveyInterpolator interpolator)
{
    /// <summary>
    /// Match the precision Marduk's auto-calc uses
    /// (<c>MardukSurveyAutoCalculator.DefaultPrecision</c>) so the
    /// derived numbers round to the same decimal places as the
    /// per-station <c>VerticalDepth</c> column.
    /// </summary>
    private const int DefaultPrecision = 6;

    /// <summary>
    /// Pull the well's TieOn + Survey rows from <paramref name="db"/>
    /// and project them into the Marduk <see cref="MardukSurveyStation"/>
    /// shape, with the previously-computed minimum-curvature columns
    /// rehydrated via <see cref="MardukSurveyStation.SetMinimumCurvature"/>.
    /// Returns <c>null</c> when the resulting station list has fewer
    /// than two entries (interpolation requires a bracketing pair).
    /// In normal flow that means the well has no Survey rows yet — the
    /// tie-on is auto-created when the Well is created, so it is
    /// effectively always present.
    /// </summary>
    public async Task<List<MardukSurveyStation>?> LoadStationsAsync(
        TenantDbContext db, int wellId, CancellationToken ct)
    {
        var tieOnRow = await db.TieOns
            .AsNoTracking()
            .Where(t => t.WellId == wellId)
            .OrderBy(t => t.Id)
            .Select(t => new
            {
                t.Depth, t.Inclination, t.Azimuth,
                t.Northing, t.Easting,
                t.VerticalReference, t.SubSeaReference, t.VerticalSectionDirection,
            })
            .FirstOrDefaultAsync(ct);

        var surveyRows = await db.Surveys
            .AsNoTracking()
            .Where(s => s.WellId == wellId)
            .OrderBy(s => s.Depth)
            .Select(s => new
            {
                s.Depth, s.Inclination, s.Azimuth,
                s.VerticalDepth, s.SubSea, s.North, s.East,
                s.DoglegSeverity, s.VerticalSection,
                s.Northing, s.Easting, s.Build, s.Turn,
            })
            .ToListAsync(ct);

        var stations = new List<MardukSurveyStation>(capacity: surveyRows.Count + 1);

        if (tieOnRow is not null)
        {
            // Use Marduk's own conversion — TieOn.ToSurveyStation
            // populates north/east/verticalDepth/subSea/etc. from the
            // tie-on's reference fields, so no synthesised values
            // here. This is the same path MardukSurveyAutoCalculator
            // implicitly uses inside ISurveyCalculator.Process.
            var mardukTieOn = new MardukTieOn(
                depth:                    tieOnRow.Depth,
                inclination:              tieOnRow.Inclination,
                azimuth:                  tieOnRow.Azimuth,
                northing:                 tieOnRow.Northing,
                easting:                  tieOnRow.Easting,
                verticalReference:        tieOnRow.VerticalReference,
                subSeaReference:          tieOnRow.SubSeaReference,
                verticalSectionDirection: tieOnRow.VerticalSectionDirection);
            stations.Add(mardukTieOn.ToSurveyStation());
        }

        foreach (var r in surveyRows)
        {
            var s = new MardukSurveyStation(r.Depth, r.Inclination, r.Azimuth);
            // Rehydrate the trajectory state — the interpolator reads
            // previous.VerticalDepth / North / East / etc. when it
            // bridges between bracketing stations.
            s.SetMinimumCurvature(
                north:           r.North,
                east:            r.East,
                verticalDepth:   r.VerticalDepth,
                subSea:          r.SubSea,
                doglegSeverity:  r.DoglegSeverity,
                verticalSection: r.VerticalSection,
                northing:        r.Northing,
                easting:         r.Easting,
                build:           r.Build,
                turn:            r.Turn);
            stations.Add(s);
        }

        // Interpolator needs a bracketing pair — fewer than two
        // stations means no envelope. In practice the tie-on is
        // always present, so this fires when the well has no
        // surveys at all.
        return stations.Count < 2 ? null : stations;
    }

    /// <summary>
    /// Synchronous helper — given pre-loaded stations, return the
    /// (fromTvd, toTvd) pair for a depth interval. Returns
    /// <c>(null, null)</c> if <paramref name="stations"/> is null
    /// (insufficient stations), or if either MD lies outside the
    /// bracketing range.
    /// </summary>
    public (double? FromTvd, double? ToTvd) ResolvePair(
        List<MardukSurveyStation>? stations, double fromMd, double toMd)
    {
        if (stations is null) return (null, null);

        try
        {
            var fromStation = interpolator.InterpolateDepth(stations, fromMd, DefaultPrecision);
            var toStation   = interpolator.InterpolateDepth(stations, toMd,   DefaultPrecision);
            return (fromStation.VerticalDepth, toStation.VerticalDepth);
        }
        catch (ArgumentOutOfRangeException)
        {
            return (null, null);
        }
        catch (ArgumentException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Convenience for single-row resolution. Loads stations for the
    /// well and interpolates the (fromMd, toMd) pair. Use
    /// <see cref="LoadStationsAsync"/> + <see cref="ResolvePair"/>
    /// directly when projecting many rows in one request.
    /// </summary>
    public async Task<(double? FromTvd, double? ToTvd)> ResolveAsync(
        TenantDbContext db, int wellId, double fromMd, double toMd, CancellationToken ct)
    {
        var stations = await LoadStationsAsync(db, wellId, ct);
        return ResolvePair(stations, fromMd, toMd);
    }
}

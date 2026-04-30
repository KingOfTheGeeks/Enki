using System.ComponentModel.DataAnnotations;
using SDI.Enki.Shared.Comments;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Logs;
using SDI.Enki.Shared.Runs;
using SDI.Enki.Shared.Shots;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.Shared.Wells;
using SDI.Enki.Shared.Wells.CommonMeasures;
using SDI.Enki.Shared.Wells.Formations;
using SDI.Enki.Shared.Wells.TieOns;
using SDI.Enki.Shared.Wells.Tubulars;

namespace SDI.Enki.WebApi.Tests.Validation;

/// <summary>
/// Direct DataAnnotations exercise for every Create / Update / Set
/// DTO in <c>SDI.Enki.Shared</c>. The <c>[ApiController]</c>
/// attribute on every controller wires the framework's automatic
/// ModelState check, so each DTO's annotation set IS the API's
/// 400-level validation contract; if these tests pass and the
/// attribute is applied, malformed payloads can't reach the
/// controller body.
///
/// <para>
/// Test technique: <see cref="Validator.TryValidateObject"/> with
/// <c>validateAllProperties: true</c> — runs every annotation on
/// every property. Returns the validation results so tests can
/// assert specific member names appear in the failure set.
/// </para>
///
/// <para>
/// One test per domain covers the negative path (invalid payload
/// produces non-empty error list) and the positive path (a sane
/// payload validates clean). Domain-specific edge cases (e.g.
/// FromVertical &lt;= ToVertical) are enforced by the controller,
/// not here, and don't show up.
/// </para>
/// </summary>
public class DtoValidationTests
{
    /// <summary>
    /// Validate a positional record's constructor-parameter
    /// attributes the same way ASP.NET Core's MVC framework does
    /// for record-shaped models. The plain
    /// <see cref="Validator.TryValidateObject"/> only walks
    /// synthesized properties; without <c>[property:]</c> targets
    /// the attribute lands on the parameter, not the property, so
    /// the standalone validator misses it. Repo convention places
    /// attributes on parameters (see <c>ProvisionTenantDto</c>'s
    /// doc comment), so we have to reflect on the primary
    /// constructor here.
    /// </summary>
    private static IList<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        var type    = dto.GetType();

        // Primary constructor — for records, that's the one with the
        // most parameters (matches the positional declaration).
        var primaryCtor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (primaryCtor is null) return results;

        foreach (var param in primaryCtor.GetParameters())
        {
            // Pull the synthesized property by name to read the
            // current value off the DTO instance.
            var prop = type.GetProperty(
                param.Name!,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);
            if (prop is null) continue;

            var value     = prop.GetValue(dto);
            var paramCtx  = new ValidationContext(dto) { MemberName = param.Name };
            var paramAttrs = param.GetCustomAttributes(typeof(ValidationAttribute), inherit: true)
                                  .Cast<ValidationAttribute>();

            foreach (var attr in paramAttrs)
            {
                var result = attr.GetValidationResult(value, paramCtx);
                if (result is not null && result != ValidationResult.Success)
                    results.Add(result);
            }
        }
        return results;
    }

    private static bool HasErrorFor(IList<ValidationResult> results, string memberName) =>
        results.Any(r => r.MemberNames.Contains(memberName));

    // ---------- Tenants ----------

    [Fact]
    public void ProvisionTenantDto_Empty_FailsValidation()
    {
        var dto = new ProvisionTenantDto(Code: "", Name: "");
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(ProvisionTenantDto.Code)));
        Assert.True(HasErrorFor(results, nameof(ProvisionTenantDto.Name)));
    }

    [Fact]
    public void ProvisionTenantDto_BadCodeFormat_FailsValidation()
    {
        var dto = new ProvisionTenantDto(Code: "lowercase", Name: "Acme");
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(ProvisionTenantDto.Code)));
    }

    [Fact]
    public void ProvisionTenantDto_BadEmail_FailsValidation()
    {
        var dto = new ProvisionTenantDto(
            Code: "ACME", Name: "Acme",
            ContactEmail: "not-an-email");
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(ProvisionTenantDto.ContactEmail)));
    }

    [Fact]
    public void UpdateTenantDto_Empty_FailsValidation()
    {
        var dto = new UpdateTenantDto(Name: "", RowVersion: null);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(UpdateTenantDto.Name)));
    }

    [Fact]
    public void Tenants_ValidPayload_Passes()
    {
        Assert.Empty(Validate(new ProvisionTenantDto(Code: "ACME", Name: "Acme Corp")));
        // RowVersion is [Required] but a valid base64 string passes the
        // attribute (controllers verify token freshness against SQL
        // Server's rowversion column, which DataAnnotations can't see).
        Assert.Empty(Validate(new UpdateTenantDto(Name: "Acme Corp", RowVersion: "AAAAAAAAAAE=")));
    }

    // ---------- Jobs ----------

    [Fact]
    public void CreateJobDto_Empty_FailsValidation()
    {
        var dto = new CreateJobDto(Name: "", Description: "", UnitSystem: "");
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateJobDto.Name)));
        Assert.True(HasErrorFor(results, nameof(CreateJobDto.Description)));
        Assert.True(HasErrorFor(results, nameof(CreateJobDto.UnitSystem)));
    }

    [Fact]
    public void CreateJobDto_OversizedName_FailsValidation()
    {
        var tooLong = new string('x', 51);   // Name capped at 50
        var dto = new CreateJobDto(Name: tooLong, Description: "ok", UnitSystem: "Field");
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateJobDto.Name)));
    }

    [Fact]
    public void CreateJobDto_ValidPayload_Passes()
    {
        var dto = new CreateJobDto(Name: "Crest-22-14H", Description: "ok", UnitSystem: "Field");
        Assert.Empty(Validate(dto));
    }

    // ---------- Wells ----------

    [Fact]
    public void CreateWellDto_Empty_FailsValidation()
    {
        var dto = new CreateWellDto(Name: "", Type: "");
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateWellDto.Name)));
        Assert.True(HasErrorFor(results, nameof(CreateWellDto.Type)));
    }

    [Fact]
    public void CreateWellDto_ValidPayload_Passes()
    {
        Assert.Empty(Validate(new CreateWellDto(Name: "Lone Star 14H", Type: "Target")));
    }

    // ---------- Surveys ----------

    [Fact]
    public void CreateSurveyDto_OutOfRangeAngles_FailsValidation()
    {
        // Inclination must be 0..180; Azimuth must be 0..360.
        var dto = new CreateSurveyDto(Depth: 100, Inclination: 200, Azimuth: 400);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateSurveyDto.Inclination)));
        Assert.True(HasErrorFor(results, nameof(CreateSurveyDto.Azimuth)));
    }

    [Fact]
    public void CreateSurveyDto_NegativeAzimuth_FailsValidation()
    {
        var dto = new CreateSurveyDto(Depth: 100, Inclination: 30, Azimuth: -1);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateSurveyDto.Azimuth)));
    }

    [Fact]
    public void CreateSurveyDto_ValidPayload_Passes()
    {
        Assert.Empty(Validate(new CreateSurveyDto(Depth: 100, Inclination: 0, Azimuth: 0)));
        Assert.Empty(Validate(new CreateSurveyDto(Depth: 100, Inclination: 90, Azimuth: 180)));
    }

    [Fact]
    public void CreateSurveysDto_EmptyList_FailsValidation()
    {
        // Bulk-create rejects empty payloads — Marduk's calc needs at
        // least one station to do anything meaningful.
        var dto = new CreateSurveysDto(Stations: new List<CreateSurveyDto>());
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateSurveysDto.Stations)));
    }

    // ---------- TieOns ----------

    [Fact]
    public void CreateTieOnDto_OutOfRangeAngles_FailsValidation()
    {
        var dto = new CreateTieOnDto(Depth: 0, Inclination: -1, Azimuth: 361);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateTieOnDto.Inclination)));
        Assert.True(HasErrorFor(results, nameof(CreateTieOnDto.Azimuth)));
    }

    [Fact]
    public void CreateTieOnDto_ValidPayload_Passes()
    {
        Assert.Empty(Validate(new CreateTieOnDto(Depth: 0, Inclination: 0, Azimuth: 0)));
    }

    // ---------- Tubulars ----------

    [Fact]
    public void CreateTubularDto_EmptyType_FailsValidation()
    {
        var dto = new CreateTubularDto(
            Type: "", Order: 0, FromMeasured: 0, ToMeasured: 100,
            Diameter: 0.1, Weight: 50);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateTubularDto.Type)));
    }

    [Fact]
    public void CreateTubularDto_ValidPayload_Passes()
    {
        var dto = new CreateTubularDto(
            Type: "Casing", Order: 0, FromMeasured: 0, ToMeasured: 100,
            Diameter: 0.244475, Weight: 69.94, Name: "Surface casing");
        Assert.Empty(Validate(dto));
    }

    // ---------- Formations ----------

    [Fact]
    public void CreateFormationDto_EmptyName_FailsValidation()
    {
        var dto = new CreateFormationDto(
            Name: "", FromVertical: 0, ToVertical: 100, Resistance: 10);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateFormationDto.Name)));
    }

    [Fact]
    public void CreateFormationDto_ValidPayload_Passes()
    {
        var dto = new CreateFormationDto(
            Name: "Eagle Ford", FromVertical: 1000, ToVertical: 1500, Resistance: 8);
        Assert.Empty(Validate(dto));
    }

    // ---------- CommonMeasures ----------

    [Fact]
    public void CreateCommonMeasureDto_ValidPayload_Passes()
    {
        Assert.Empty(Validate(
            new CreateCommonMeasureDto(FromVertical: 0, ToVertical: 100, Value: 1.0)));
    }

    // ---------- Magnetics ----------

    [Fact]
    public void SetMagneticsDto_OutOfRangeAngles_FailsValidation()
    {
        // Dip must be -90..90; Declination must be -180..180; BTotal 0..100k.
        var dto = new SetMagneticsDto(BTotal: 200_000, Dip: 100, Declination: 200, RowVersion: null);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(SetMagneticsDto.BTotal)));
        Assert.True(HasErrorFor(results, nameof(SetMagneticsDto.Dip)));
        Assert.True(HasErrorFor(results, nameof(SetMagneticsDto.Declination)));
    }

    [Fact]
    public void SetMagneticsDto_ValidPayload_Passes()
    {
        // PERMIAN seed values — positive dip + east declination.
        Assert.Empty(Validate(new SetMagneticsDto(BTotal: 50_300, Dip: 63, Declination: 5, RowVersion: null)));
        // CARNARVON-style negative dip (southern hemisphere).
        Assert.Empty(Validate(new SetMagneticsDto(BTotal: 57_000, Dip: -50, Declination: 1, RowVersion: null)));
    }

    // ---------- Runs ----------

    [Fact]
    public void CreateRunDto_Empty_FailsValidation()
    {
        var dto = new CreateRunDto(
            Name: "", Description: "", Type: "",
            StartDepth: 0, EndDepth: 0,
            // Magnetics required fields supplied with valid values so
            // the test focuses on the Name/Description/Type errors.
            BTotalNanoTesla: 50_000, DipDegrees: 60, DeclinationDegrees: 0);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateRunDto.Name)));
        Assert.True(HasErrorFor(results, nameof(CreateRunDto.Description)));
        Assert.True(HasErrorFor(results, nameof(CreateRunDto.Type)));
    }

    [Fact]
    public void CreateRunDto_NegativeDepth_FailsValidation()
    {
        var dto = new CreateRunDto(
            Name: "R1", Description: "ok", Type: "Gradient",
            StartDepth: -1, EndDepth: 100,
            BTotalNanoTesla: 50_000, DipDegrees: 60, DeclinationDegrees: 0);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateRunDto.StartDepth)));
    }

    [Fact]
    public void CreateRunDto_ValidPayload_Passes()
    {
        var dto = new CreateRunDto(
            Name: "R1", Description: "ok", Type: "Gradient",
            StartDepth: 100, EndDepth: 200,
            BTotalNanoTesla: 50_000, DipDegrees: 60, DeclinationDegrees: 0,
            BridleLength: 12, CurrentInjection: 5);
        Assert.Empty(Validate(dto));
    }

    [Fact]
    public void CreateRunDto_OutOfRangeBTotal_FailsValidation()
    {
        var dto = new CreateRunDto(
            Name: "R1", Description: "ok", Type: "Gradient",
            StartDepth: 100, EndDepth: 200,
            BTotalNanoTesla: 999, DipDegrees: 60, DeclinationDegrees: 0);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateRunDto.BTotalNanoTesla)));
    }

    [Fact]
    public void CreateRunDto_OutOfRangeDip_FailsValidation()
    {
        var dto = new CreateRunDto(
            Name: "R1", Description: "ok", Type: "Gradient",
            StartDepth: 100, EndDepth: 200,
            BTotalNanoTesla: 50_000, DipDegrees: 200, DeclinationDegrees: 0);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateRunDto.DipDegrees)));
    }

    // ---------- Shots (Phase 2 reshape) ----------
    //
    // Phase 2 collapses the legacy CreateGradientShotDto's 11-parameter
    // shape (ToolUptime / ShotTime / TimeStart / TimeEnd / NumberOfMags /
    // Frequency / Bandwidth / SampleFrequency / SampleCount + identity)
    // down to identity + optional config payload — Marduk consumes the
    // raw binary + config and produces those structured fields server-
    // side rather than the client supplying them. The validation
    // surface shrinks accordingly.

    [Fact]
    public void CreateShotDto_Empty_FailsValidation()
    {
        var dto = new CreateShotDto(ShotName: "", FileTime: default);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateShotDto.ShotName)));
    }

    [Fact]
    public void CreateShotDto_OversizedShotName_FailsValidation()
    {
        var tooLong = new string('s', 201);  // ShotName capped at 200
        var dto = new CreateShotDto(ShotName: tooLong, FileTime: DateTimeOffset.UtcNow);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateShotDto.ShotName)));
    }

    [Fact]
    public void CreateShotDto_ValidPayload_Passes()
    {
        // Phase-2 reshape: CreateShotDto is identity-only.
        // Calibration is run-based (via the run's Tool); processing
        // config is a typed Marduk class populated server-side.
        var dto = new CreateShotDto(
            ShotName: "shot-1A",
            FileTime: DateTimeOffset.UtcNow);
        Assert.Empty(Validate(dto));
    }

    [Fact]
    public void UpdateShotDto_MissingRowVersion_FailsValidation()
    {
        var dto = new UpdateShotDto(
            ShotName: "shot-1A",
            FileTime: DateTimeOffset.UtcNow,
            CalibrationId: null,
            RowVersion: null);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(UpdateShotDto.RowVersion)));
    }

    [Fact]
    public void UpdateShotDto_ValidPayload_Passes()
    {
        var dto = new UpdateShotDto(
            ShotName: "shot-1A",
            FileTime: DateTimeOffset.UtcNow,
            CalibrationId: 42,
            RowVersion: "AAAAAAAAAAE=");
        Assert.Empty(Validate(dto));
    }

    // ---------- Logs (Phase 2 reshape) ----------

    [Fact]
    public void CreateLogDto_Empty_FailsValidation()
    {
        var dto = new CreateLogDto(ShotName: "", FileTime: default);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateLogDto.ShotName)));
    }

    [Fact]
    public void CreateLogDto_ValidPayload_Passes()
    {
        var dto = new CreateLogDto(
            ShotName: "log-1",
            FileTime: DateTimeOffset.UtcNow,
            CalibrationId: 7);
        Assert.Empty(Validate(dto));
    }

    [Fact]
    public void UpdateLogDto_MissingRowVersion_FailsValidation()
    {
        var dto = new UpdateLogDto(
            ShotName: "log-1",
            FileTime: DateTimeOffset.UtcNow,
            CalibrationId: null,
            RowVersion: null);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(UpdateLogDto.RowVersion)));
    }

    [Fact]
    public void UpdateLogDto_ValidPayload_Passes()
    {
        var dto = new UpdateLogDto(
            ShotName: "log-1",
            FileTime: DateTimeOffset.UtcNow,
            CalibrationId: 7,
            RowVersion: "AAAAAAAAAAE=");
        Assert.Empty(Validate(dto));
    }

    // ---------- Comments (Phase 2 — under Shot) ----------

    [Fact]
    public void CreateCommentDto_Empty_FailsValidation()
    {
        var dto = new CreateCommentDto(Text: "");
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateCommentDto.Text)));
    }

    [Fact]
    public void CreateCommentDto_OversizedText_FailsValidation()
    {
        var tooLong = new string('c', 4001);  // Text capped at 4000
        var dto = new CreateCommentDto(Text: tooLong);
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(CreateCommentDto.Text)));
    }

    [Fact]
    public void CreateCommentDto_ValidPayload_Passes()
    {
        Assert.Empty(Validate(new CreateCommentDto(Text: "Caught a noisy mag at this stand.")));
    }

    // ---------- SurveyCalculationRequest ----------

    [Fact]
    public void SurveyCalculationRequestDto_OutOfRangePrecision_FailsValidation()
    {
        var dto = new SurveyCalculationRequestDto(
            MetersToCalculateDegreesOver: 30,
            Precision: 99);              // capped at 15
        var results = Validate(dto);
        Assert.True(HasErrorFor(results, nameof(SurveyCalculationRequestDto.Precision)));
    }

    [Fact]
    public void SurveyCalculationRequestDto_DefaultsAreValid()
    {
        Assert.Empty(Validate(new SurveyCalculationRequestDto()));
    }
}

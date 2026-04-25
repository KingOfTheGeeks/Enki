namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Response shape for <c>POST /tenants/{c}/jobs/{j}/wells/{w}/surveys/import</c>.
/// Carries the importer's audit trail (detected format / unit / well
/// name) plus the count of rows that landed in the DB and the warning
/// notes the importer emitted while parsing the file. Errors that
/// prevent any rows from landing surface as an HTTP 4xx with a
/// ProblemDetails body — the result DTO is only returned on success.
/// </summary>
public sealed record SurveyImportResultDto(
    int WellId,
    string DetectedFormat,
    string DetectedDepthUnit,
    bool DepthUnitWasDetected,
    string? WellNameFromFile,
    int TieOnsCreated,
    int SurveysImported,
    DateTimeOffset ImportedAt,
    IReadOnlyList<SurveyImportNoteDto> Notes);

/// <summary>
/// Wire-friendly projection of a single <c>AMR.Core.IO.ImportNote</c>:
/// stable code, severity, message, optional source-line number.
/// </summary>
public sealed record SurveyImportNoteDto(
    string Severity,    // "Warning" | "Error"
    string Code,        // matches AMR.Core.IO.ImportNoteCodes constants
    string Message,
    int? LineNumber);

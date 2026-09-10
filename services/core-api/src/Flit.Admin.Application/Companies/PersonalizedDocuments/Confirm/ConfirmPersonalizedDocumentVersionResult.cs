namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Confirm;

/// <summary>Desenlace de la confirmación de una versión.</summary>
public enum ConfirmPersonalizedDocumentVersionOutcome
{
    Activated,
    NotFound,
    ChannelNotEnabled,
    Rejected,
}

/// <summary>
/// Resultado de la confirmación: <see cref="ConfirmPersonalizedDocumentVersionOutcome.Activated"/>
/// (200, activo) · <c>NotFound</c> (404 — incluye el negativo de aislamiento: tenant ajeno) ·
/// <c>ChannelNotEnabled</c> (409 <c>canal_no_habilitado</c>) · <c>Rejected</c> (422 — la versión queda
/// en <c>rechazado</c>, AC3).
/// </summary>
public sealed class ConfirmPersonalizedDocumentVersionResult
{
    private ConfirmPersonalizedDocumentVersionResult(
        ConfirmPersonalizedDocumentVersionOutcome outcome,
        int? version,
        string? sha256,
        int? pageCount,
        IReadOnlyList<PersonalizedDocumentValidationError> errors)
    {
        Outcome = outcome;
        Version = version;
        Sha256 = sha256;
        PageCount = pageCount;
        Errors = errors;
    }

    public ConfirmPersonalizedDocumentVersionOutcome Outcome { get; }
    public int? Version { get; }
    public string? Sha256 { get; }
    public int? PageCount { get; }
    public IReadOnlyList<PersonalizedDocumentValidationError> Errors { get; }

    public static ConfirmPersonalizedDocumentVersionResult Activated(int version, string sha256, int pageCount) =>
        new(ConfirmPersonalizedDocumentVersionOutcome.Activated, version, sha256, pageCount, []);

    public static ConfirmPersonalizedDocumentVersionResult NotFound() =>
        new(ConfirmPersonalizedDocumentVersionOutcome.NotFound, null, null, null, []);

    public static ConfirmPersonalizedDocumentVersionResult ChannelNotEnabled() =>
        new(ConfirmPersonalizedDocumentVersionOutcome.ChannelNotEnabled, null, null, null, []);

    public static ConfirmPersonalizedDocumentVersionResult Rejected(IReadOnlyList<PersonalizedDocumentValidationError> errors) =>
        new(ConfirmPersonalizedDocumentVersionOutcome.Rejected, null, null, null, errors);
}

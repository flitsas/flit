namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Activate;

/// <summary>Desenlace de la activación/reactivación de una versión (HU #11314).</summary>
public enum ActivatePersonalizedDocumentVersionOutcome
{
    Activated,
    NotFound,
    ChannelNotEnabled,
    VersionNotActivable,
}

/// <summary>
/// Resultado de activar/reactivar una versión: <see cref="ActivatePersonalizedDocumentVersionOutcome.Activated"/>
/// (200) · <c>NotFound</c> (404 — incluye el negativo de aislamiento: tenant ajeno) ·
/// <c>ChannelNotEnabled</c> (409 <c>canal_no_habilitado</c>) · <c>VersionNotActivable</c> (409
/// <c>version_no_activable</c> — la versión está en <c>pendiente</c> o <c>rechazado</c>).
/// </summary>
public sealed class ActivatePersonalizedDocumentVersionResult
{
    private ActivatePersonalizedDocumentVersionResult(ActivatePersonalizedDocumentVersionOutcome outcome, int? version)
    {
        Outcome = outcome;
        Version = version;
    }

    public ActivatePersonalizedDocumentVersionOutcome Outcome { get; }
    public int? Version { get; }

    public static ActivatePersonalizedDocumentVersionResult Activated(int version) =>
        new(ActivatePersonalizedDocumentVersionOutcome.Activated, version);

    public static ActivatePersonalizedDocumentVersionResult NotFound() =>
        new(ActivatePersonalizedDocumentVersionOutcome.NotFound, null);

    public static ActivatePersonalizedDocumentVersionResult ChannelNotEnabled() =>
        new(ActivatePersonalizedDocumentVersionOutcome.ChannelNotEnabled, null);

    public static ActivatePersonalizedDocumentVersionResult VersionNotActivable() =>
        new(ActivatePersonalizedDocumentVersionOutcome.VersionNotActivable, null);
}

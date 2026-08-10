namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Deactivate;

/// <summary>Desenlace de «volver al documento del sistema» (HU #11314).</summary>
public enum DeactivatePersonalizedDocumentOutcome
{
    /// <summary>204 — sin versión activa (idempotente: ya lo estaba, o se acaba de retirar una).</summary>
    Deactivated,

    ChannelNotEnabled,
}

/// <summary>
/// Resultado de desactivar el reemplazo: <see cref="DeactivatePersonalizedDocumentOutcome.Deactivated"/>
/// (204, siempre — incluso si no había nada que desactivar) · <c>ChannelNotEnabled</c> (409
/// <c>canal_no_habilitado</c>).
/// </summary>
public sealed class DeactivatePersonalizedDocumentResult
{
    private DeactivatePersonalizedDocumentResult(DeactivatePersonalizedDocumentOutcome outcome)
    {
        Outcome = outcome;
    }

    public DeactivatePersonalizedDocumentOutcome Outcome { get; }

    public static DeactivatePersonalizedDocumentResult Deactivated() =>
        new(DeactivatePersonalizedDocumentOutcome.Deactivated);

    public static DeactivatePersonalizedDocumentResult ChannelNotEnabled() =>
        new(DeactivatePersonalizedDocumentOutcome.ChannelNotEnabled);
}

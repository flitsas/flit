namespace Flit.Admin.Application.DocumentTypes.ReactivateDocumentType;

/// <summary>Desenlace de la reactivación de un tipo de documento.</summary>
public enum ReactivateDocumentTypeOutcome
{
    /// <summary>Reactivado correctamente (o ya estaba activo) → HTTP 204.</summary>
    Reactivated,

    /// <summary>El id no existe → HTTP 404.</summary>
    NotFound,
}

/// <summary>Resultado de la reactivación; el <see cref="Outcome"/> determina el status HTTP.</summary>
public sealed class ReactivateDocumentTypeResult
{
    private ReactivateDocumentTypeResult(ReactivateDocumentTypeOutcome outcome) => Outcome = outcome;

    public ReactivateDocumentTypeOutcome Outcome { get; }

    public static ReactivateDocumentTypeResult Reactivated { get; } = new(ReactivateDocumentTypeOutcome.Reactivated);

    public static ReactivateDocumentTypeResult NotFound { get; } = new(ReactivateDocumentTypeOutcome.NotFound);
}

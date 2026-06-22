namespace Flit.Admin.Application.DocumentOrderOverrides.DeleteDocumentOrderOverride;

/// <summary>Desenlace del borrado de un override (AC6).</summary>
public enum DeleteDocumentOrderOverrideOutcome
{
    /// <summary>Eliminado físicamente → HTTP 204.</summary>
    Deleted,

    /// <summary>El id no existe → HTTP 404.</summary>
    NotFound,
}

/// <summary>Resultado del borrado; el <see cref="Outcome"/> determina el status HTTP.</summary>
public sealed class DeleteDocumentOrderOverrideResult
{
    private DeleteDocumentOrderOverrideResult(DeleteDocumentOrderOverrideOutcome outcome) =>
        Outcome = outcome;

    public DeleteDocumentOrderOverrideOutcome Outcome { get; }

    public static DeleteDocumentOrderOverrideResult Deleted { get; } =
        new(DeleteDocumentOrderOverrideOutcome.Deleted);

    public static DeleteDocumentOrderOverrideResult NotFound { get; } =
        new(DeleteDocumentOrderOverrideOutcome.NotFound);
}

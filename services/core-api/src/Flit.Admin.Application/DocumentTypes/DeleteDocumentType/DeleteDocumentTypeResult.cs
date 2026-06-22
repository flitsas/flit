namespace Flit.Admin.Application.DocumentTypes.DeleteDocumentType;

/// <summary>Desenlace del soft-delete de un tipo de documento (AC4/AC6).</summary>
public enum DeleteDocumentTypeOutcome
{
    /// <summary>Desactivado correctamente → HTTP 204.</summary>
    Deleted,

    /// <summary>El id no existe → HTTP 404.</summary>
    NotFound,

    /// <summary>Tiene asociaciones en procedure_document_requirements → HTTP 409.</summary>
    HasAssociations,
}

/// <summary>Resultado del soft-delete; el <see cref="Outcome"/> determina el status HTTP.</summary>
public sealed class DeleteDocumentTypeResult
{
    /// <summary>Mensaje 409 exacto del AC6.</summary>
    public const string HasAssociationsMessage =
        "El tipo de documento tiene asociaciones activas y no puede desactivarse";

    private DeleteDocumentTypeResult(DeleteDocumentTypeOutcome outcome) => Outcome = outcome;

    public DeleteDocumentTypeOutcome Outcome { get; }

    public static DeleteDocumentTypeResult Deleted { get; } = new(DeleteDocumentTypeOutcome.Deleted);

    public static DeleteDocumentTypeResult NotFound { get; } = new(DeleteDocumentTypeOutcome.NotFound);

    public static DeleteDocumentTypeResult HasAssociations { get; } = new(DeleteDocumentTypeOutcome.HasAssociations);
}

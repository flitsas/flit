using Flit.Admin.Domain.DocumentTypes;

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

    private DeleteDocumentTypeResult(
        DeleteDocumentTypeOutcome outcome,
        IReadOnlyList<DocumentTypeAssociationRef> associations)
    {
        Outcome = outcome;
        Associations = associations;
    }

    public DeleteDocumentTypeOutcome Outcome { get; }

    /// <summary>
    /// Trámites que usan el documento (solo en <see cref="DeleteDocumentTypeOutcome.HasAssociations"/>);
    /// vacío en los demás casos. Alimenta el 409 accionable de la consola.
    /// </summary>
    public IReadOnlyList<DocumentTypeAssociationRef> Associations { get; }

    public static DeleteDocumentTypeResult Deleted { get; } =
        new(DeleteDocumentTypeOutcome.Deleted, []);

    public static DeleteDocumentTypeResult NotFound { get; } =
        new(DeleteDocumentTypeOutcome.NotFound, []);

    public static DeleteDocumentTypeResult HasAssociations(
        IReadOnlyList<DocumentTypeAssociationRef> associations) =>
        new(DeleteDocumentTypeOutcome.HasAssociations, associations);
}

using Flit.Admin.Domain.DocumentRequirements;

namespace Flit.Admin.Application.DocumentRequirements;

/// <summary>
/// Respuesta pública de una asociación trámite ↔ documento (HU #10195). Serializada en
/// camelCase: <c>{ id, procedureTypeId, documentTypeId, ordenDefault, obligatorio,
/// documento: { codigo, nombre, estado } }</c>. <c>documento</c> enriquece el ítem con
/// los datos del tipo de documento asociado (join a <c>document_types</c>).
/// </summary>
public sealed record ProcedureDocumentRequirementResponse(
    Guid Id,
    Guid ProcedureTypeId,
    Guid DocumentTypeId,
    short OrdenDefault,
    bool Obligatorio,
    DocumentRefResponse Documento)
{
    public static ProcedureDocumentRequirementResponse From(ProcedureDocumentRequirementItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new ProcedureDocumentRequirementResponse(
            item.Id,
            item.ProcedureTypeId,
            item.DocumentTypeId,
            item.OrdenDefault,
            item.Obligatorio,
            new DocumentRefResponse(
                item.DocumentCode,
                item.DocumentName,
                item.DocumentIsActive ? EstadoActivo : EstadoInactivo));
    }

    public const string EstadoActivo = "activo";
    public const string EstadoInactivo = "inactivo";
}

/// <summary>Datos del tipo de documento asociado embebidos en la respuesta.</summary>
public sealed record DocumentRefResponse(string Codigo, string Nombre, string Estado);

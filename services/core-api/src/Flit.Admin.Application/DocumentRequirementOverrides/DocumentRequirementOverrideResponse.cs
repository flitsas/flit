using Flit.Admin.Domain.DocumentRequirementOverrides;

namespace Flit.Admin.Application.DocumentRequirementOverrides;

/// <summary>
/// Respuesta pública de un override de obligatoriedad por OT (HU #10198). Serializada en
/// camelCase: <c>{ id, procedureTypeId, documentTypeId, transitOfficeId, estado,
/// documento: { codigo, nombre } }</c>. <c>estado</c> es REQUIRED | OPTIONAL | NOT_APPLICABLE.
/// </summary>
public sealed record DocumentRequirementOverrideResponse(
    Guid Id,
    Guid ProcedureTypeId,
    Guid DocumentTypeId,
    Guid TransitOfficeId,
    string Estado,
    RequirementOverrideDocumentRef Documento)
{
    public static DocumentRequirementOverrideResponse From(DocumentRequirementOverrideItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new DocumentRequirementOverrideResponse(
            item.Id,
            item.ProcedureTypeId,
            item.DocumentTypeId,
            item.TransitOfficeId,
            item.RequirementState,
            new RequirementOverrideDocumentRef(item.DocumentCode, item.DocumentName));
    }
}

/// <summary>Datos mínimos del tipo de documento embebidos en la respuesta.</summary>
public sealed record RequirementOverrideDocumentRef(string Codigo, string Nombre);

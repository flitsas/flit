using Flit.Admin.Domain.DocumentOrderOverrides;

namespace Flit.Admin.Application.DocumentOrderOverrides;

/// <summary>
/// Respuesta pública de un override de orden documental (HU #10196). Serializada en
/// camelCase: <c>{ id, procedureTypeId, documentTypeId, scope, scopeRefId, orden,
/// documento: { codigo, nombre } }</c>. <c>documento</c> enriquece el ítem con los datos
/// del tipo de documento asociado (join a <c>document_types</c>).
/// </summary>
public sealed record DocumentOrderOverrideResponse(
    Guid Id,
    Guid ProcedureTypeId,
    Guid DocumentTypeId,
    string Scope,
    Guid ScopeRefId,
    short Orden,
    OverrideDocumentRef Documento)
{
    public static DocumentOrderOverrideResponse From(DocumentOrderOverrideItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new DocumentOrderOverrideResponse(
            item.Id,
            item.ProcedureTypeId,
            item.DocumentTypeId,
            item.ScopeType,
            item.ScopeRefId,
            item.Orden,
            new OverrideDocumentRef(item.DocumentCode, item.DocumentName));
    }
}

/// <summary>Datos mínimos del tipo de documento embebidos en la respuesta del override.</summary>
public sealed record OverrideDocumentRef(string Codigo, string Nombre);

using Flit.Admin.Domain.ProcedureSnapshots;

namespace Flit.Admin.Application.ProcedureSnapshots;

/// <summary>
/// Respuesta pública de un documento requerido del snapshot de un trámite (HU #10197,
/// AC4). Reproduce los campos congelados en el momento de creación; serializada en
/// camelCase: <c>{ documentTypeId, codigo, nombre, obligatorio, ordenResuelto,
/// nivelAplicado }</c>.
/// </summary>
public sealed record ProcedureDocumentRequirementSnapshotResponse(
    Guid DocumentTypeId,
    string Codigo,
    string Nombre,
    bool Obligatorio,
    short OrdenResuelto,
    string NivelAplicado)
{
    public static ProcedureDocumentRequirementSnapshotResponse From(ProcedureDocumentSnapshotItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new ProcedureDocumentRequirementSnapshotResponse(
            item.DocumentTypeId,
            item.Codigo,
            item.Nombre,
            item.Obligatorio,
            item.OrdenResuelto,
            item.NivelAplicado);
    }
}

using Flit.Admin.Domain.DocumentOrderOverrides;

namespace Flit.Admin.Application.DocumentOrderOverrides;

/// <summary>
/// Respuesta pública de una fila de la matriz documental resuelta (HU #10196, RF18).
/// Serializada en camelCase: <c>{ documentTypeId, codigo, nombre, obligatorio,
/// ordenResuelto, nivelAplicado }</c>. <c>nivelAplicado</c> es <c>CLIENTE</c>, <c>OT</c> o
/// <c>DEFAULT</c> según el nivel que ganó la precedencia.
/// </summary>
public sealed record ResolvedDocumentMatrixResponse(
    Guid DocumentTypeId,
    string Codigo,
    string Nombre,
    bool Obligatorio,
    short OrdenResuelto,
    string NivelAplicado)
{
    public static ResolvedDocumentMatrixResponse From(ResolvedDocumentMatrixItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new ResolvedDocumentMatrixResponse(
            item.DocumentTypeId,
            item.Codigo,
            item.Nombre,
            item.Obligatorio,
            item.OrdenResuelto,
            item.NivelAplicado);
    }
}

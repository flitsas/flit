namespace Flit.Admin.Domain.DocumentOrderOverrides;

/// <summary>
/// Read model de una fila de la matriz documental resuelta (HU #10196, RF18). Cada fila
/// corresponde a un documento del trámite con su orden final tras aplicar la precedencia
/// Cliente &gt; OT &gt; Default y el nivel que ganó la resolución.
/// </summary>
public sealed class ResolvedDocumentMatrixItem
{
    /// <summary>Tipo de documento resuelto.</summary>
    public Guid DocumentTypeId { get; init; }

    /// <summary>Código del documento — <c>document_types.code</c>.</summary>
    public string Codigo { get; init; } = string.Empty;

    /// <summary>Nombre del documento — <c>document_types.name</c>.</summary>
    public string Nombre { get; init; } = string.Empty;

    /// <summary>Obligatoriedad del documento (nivel base) — <c>is_mandatory</c>.</summary>
    public bool Obligatorio { get; init; }

    /// <summary>Orden final tras aplicar la precedencia.</summary>
    public short OrdenResuelto { get; init; }

    /// <summary>
    /// Nivel que determinó el orden: <see cref="DocumentOrderScope.Cliente"/>,
    /// <see cref="DocumentOrderScope.Ot"/> o <see cref="DocumentOrderScope.Default"/>.
    /// </summary>
    public string NivelAplicado { get; init; } = string.Empty;
}

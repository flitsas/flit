namespace Flit.Admin.Domain.OtDocumentPrecedence;

/// <summary>
/// Entrada de la lista de prelación documental de un OT (HU #10222; ampliada en la HU #11182).
/// <para>
/// Ya no representa solo una fila de <c>admin.ot_document_precedence</c>: la lista es la
/// <b>unión</b> de la matriz base del trámite, los documentos que genera el sistema y los
/// overrides guardados por el OT. Los documentos que el OT todavía no ha reordenado se devuelven
/// igual, con <see cref="Id"/> vacío y <see cref="IsConfigured"/> en <c>false</c>.
/// </para>
/// </summary>
public sealed class OtDocumentPrecedenceItem
{
    /// <summary>Id de la fila persistida, o <c>Guid.Empty</c> si el OT aún no la ha configurado.</summary>
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public Guid ProcedureTypeId { get; init; }

    public Guid DocumentTypeId { get; init; }

    /// <summary>Código del catálogo (<c>document_types.code</c>), que empareja con el tipo del adjunto.</summary>
    public string DocumentCode { get; init; } = string.Empty;

    public string DocumentName { get; init; } = string.Empty;

    public short SortOrder { get; init; }

    /// <summary>El documento lo produce FLIT (HU #11181); el gestor no lo adjunta.</summary>
    public bool IsSystemGenerated { get; init; }

    /// <summary>El OT ya guardó una posición para este documento (hay fila en la tabla).</summary>
    public bool IsConfigured { get; init; }
}

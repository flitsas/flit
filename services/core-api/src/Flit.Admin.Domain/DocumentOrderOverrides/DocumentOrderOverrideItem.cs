namespace Flit.Admin.Domain.DocumentOrderOverrides;

/// <summary>
/// Read model de un override de orden documental (HU #10196). Proyección sobre
/// <c>tramites.document_order_overrides</c> enriquecida con los datos del tipo de
/// documento (join a <c>tramites.document_types</c>). La capa de aplicación traduce los
/// nombres a la API en camelCase.
/// </summary>
public sealed class DocumentOrderOverrideItem
{
    /// <summary>Id del override — <c>document_order_overrides.id</c>.</summary>
    public Guid Id { get; init; }

    /// <summary>Tipo de trámite — <c>document_order_overrides.procedure_type_id</c>.</summary>
    public Guid ProcedureTypeId { get; init; }

    /// <summary>Tipo de documento — <c>document_order_overrides.document_type_id</c>.</summary>
    public Guid DocumentTypeId { get; init; }

    /// <summary>Ámbito del override (<c>OT</c> | <c>CLIENTE</c>) — <c>scope_type</c>.</summary>
    public string ScopeType { get; init; } = string.Empty;

    /// <summary>Referencia del ámbito (transitOfficeId o clienteId) — <c>scope_ref_id</c>.</summary>
    public Guid ScopeRefId { get; init; }

    /// <summary>Orden personalizado — <c>document_order_overrides.sort_order</c>.</summary>
    public short Orden { get; init; }

    /// <summary>Código del documento — <c>document_types.code</c>.</summary>
    public string DocumentCode { get; init; } = string.Empty;

    /// <summary>Nombre del documento — <c>document_types.name</c>.</summary>
    public string DocumentName { get; init; } = string.Empty;
}

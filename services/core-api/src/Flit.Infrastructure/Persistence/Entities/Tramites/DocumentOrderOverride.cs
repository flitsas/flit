namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Override de orden documental por OT/Cliente — <c>tramites.document_order_overrides</c>
/// (DDL HU #10155). Personaliza el orden de un documento de un trámite a nivel de
/// Organismo de Tránsito (<c>scope_type='OT'</c>) o de Cliente (<c>scope_type='CLIENTE'</c>),
/// donde <see cref="ScopeRefId"/> es el transitOfficeId o el clienteId según el ámbito
/// (HU #10196).
/// </summary>
public sealed class DocumentOrderOverride
{
    public Guid Id { get; set; }

    public Guid ProcedureTypeId { get; set; }

    public Guid DocumentTypeId { get; set; }

    /// <summary><c>OT</c> | <c>CLIENTE</c>.</summary>
    public string ScopeType { get; set; } = string.Empty;

    /// <summary>transitOfficeId (scope OT) o clienteId/tenantId (scope CLIENTE).</summary>
    public Guid? ScopeRefId { get; set; }

    public short SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}

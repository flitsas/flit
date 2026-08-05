namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Opt-out de obligatoriedad del documento de prenda por compañía + OT —
/// <c>admin.tenant_transit_office_prenda_document_policies</c>.
/// Ausencia de fila = prenda obligatoria (default). <see cref="DocumentOptional"/> = true ⇒ opcional.
/// </summary>
public sealed class TenantTransitOfficePrendaDocumentPolicy
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TransitOfficeId { get; set; }

    /// <summary>Check activo: el documento de prenda deja de ser obligatorio.</summary>
    public bool DocumentOptional { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

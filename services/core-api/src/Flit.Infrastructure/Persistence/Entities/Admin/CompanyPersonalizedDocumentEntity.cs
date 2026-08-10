namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Versión de un documento personalizado por compañía — <c>admin.company_personalized_documents</c>
/// (HU #11313, Feature #11309, ADR-0042). Tenant-scoped (RLS decorativo por <c>tenant_id</c>; el
/// aislamiento real es el <c>WHERE tenant_id</c> manual del repositorio). El PDF vive en storage
/// (<c>StoragePath</c>/<c>StorageSha256</c>), nunca en la fila. Patrón de <c>CompanyDeedEntity</c>
/// (ADR-0033).
/// </summary>
public sealed class CompanyPersonalizedDocumentEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary><c>mandato</c> | <c>tramite_virtual</c> — mismo vocabulario del <c>Tipo</c> del adjunto.</summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Incremental por <c>(tenant_id, document_type)</c>, empieza en 1.</summary>
    public int Version { get; set; }

    /// <summary><c>pendiente</c> | <c>activo</c> | <c>historico</c> | <c>rechazado</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string Filename { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;

    public string StorageSha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int? PageCount { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public Guid? ActivatedBy { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }

    public Guid? DeactivatedBy { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

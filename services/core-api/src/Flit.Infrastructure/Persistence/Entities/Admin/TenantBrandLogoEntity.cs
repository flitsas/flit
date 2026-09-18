namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Versión del logotipo de marca de una cabeza MARCA_BLANCA — <c>admin.tenant_brand_logos</c>
/// (HU #12412 AC7, ADR-0060 D1). Una fila por versión; reemplazar conserva la anterior como
/// <c>superseded</c> (patrón <c>CompanyPersonalizedDocumentEntity</c>: versión + integridad). El binario
/// vive en storage (<see cref="StoragePath"/>/<see cref="StorageSha256"/>), nunca en la fila. El motor
/// exige <c>tenant_type = MARCA_BLANCA</c> (<c>tr_tenant_brand_logos_marca_blanca</c>) y una sola versión
/// <c>active</c> por cabeza (<c>uq_tenant_brand_logos_one_active</c>).
/// </summary>
public sealed class TenantBrandLogoEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Incremental por <c>tenant_id</c>, empieza en 1, nunca se reutiliza.</summary>
    public int Version { get; set; }

    /// <summary><c>active</c> | <c>superseded</c>.</summary>
    public string Status { get; set; } = "active";

    /// <summary><c>image/png</c> | <c>image/jpeg</c> | <c>image/webp</c>, detectado por firma binaria.</summary>
    public string ContentType { get; set; } = string.Empty;

    public string Filename { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;

    /// <summary>SHA-256 hex en minúsculas (64 caracteres); ETag del endpoint público.</summary>
    public string StorageSha256 { get; set; } = string.Empty;

    public int SizeBytes { get; set; }

    public int WidthPx { get; set; }

    public int HeightPx { get; set; }

    public DateTimeOffset? SupersededAt { get; set; }

    public Guid? SupersededBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public long RowVersion { get; set; }
}

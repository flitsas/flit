namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Banner promocional -- admin.banners (HU #12238/#12239, Feature #12236). Tabla GLOBAL sin
/// tenant_id ni RLS (excepcion documentada en ADR-0058). La imagen vive en storage (ADR-0057):
/// aqui solo el path opaco y el SHA-256, nunca el binario. valid_from/valid_until son NULLABLE:
/// ambas nulas equivale a sin vigencia programada (activacion/desactivacion manual via IsActive).
/// </summary>
public sealed class Banner
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ImageStoragePath { get; set; } = string.Empty;

    public string ImageSha256 { get; set; } = string.Empty;

    public string? LinkUrl { get; set; }

    public DateTimeOffset? ValidFrom { get; set; }

    public DateTimeOffset? ValidUntil { get; set; }

    public bool IsActive { get; set; } = true;

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

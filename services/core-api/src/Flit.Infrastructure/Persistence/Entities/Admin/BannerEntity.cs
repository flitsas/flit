namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Banner promocional global — <c>admin.banners</c> (Feature #12236, ADR-0058: tabla GLOBAL
/// sin <c>tenant_id</c> ni RLS, excepcion documentada a A4/A10/A11). La imagen se sirve por
/// streaming propio (ADR-0057): aqui solo se guarda el path opaco y el SHA-256 (ETag).
/// DDL: <c>107-HU12238-admin-banners.sql</c> (HU #12238).
/// </summary>
public sealed class BannerEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ImageStoragePath { get; set; } = string.Empty;

    /// <summary>SHA-256 en hex minusculas — es el ETag del endpoint de imagen (ADR-0057).</summary>
    public string ImageSha256 { get; set; } = string.Empty;

    public string? LinkUrl { get; set; }

    /// <summary>NULL junto con <see cref="ValidUntil"/> = sin vigencia programada (HU #12238, AC2).</summary>
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

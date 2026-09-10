namespace Flit.Admin.Domain.Banners;

/// <summary>
/// Read model de un banner promocional (HU #12239). Proyección sobre <c>admin.banners</c>
/// (tabla global sin <c>tenant_id</c>, ADR-0058). <see cref="Estado"/> se calcula en la capa de
/// aplicación (<c>BannerEstadoCalculator</c>), no viaja como columna.
/// </summary>
public sealed class BannerListItem
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Id opaco de almacenamiento del binario (ADR-0057) — <c>image_storage_path</c>.</summary>
    public string ImageStoragePath { get; init; } = string.Empty;

    /// <summary>SHA-256 del binario — usado como <c>ETag</c> por el endpoint de lectura de HU3.</summary>
    public string ImageSha256 { get; init; } = string.Empty;

    public string? LinkUrl { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidUntil { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Control de concurrencia optimista — <c>row_version</c> (trigger BD).</summary>
    public long RowVersion { get; init; }
}

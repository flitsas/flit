namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Versión del logotipo de marca (HU #12412 AC7) — proyección de <c>admin.tenant_brand_logos</c>.
/// Reemplazar conserva la versión anterior como <c>superseded</c> (patrón
/// <c>CompanyPersonalizedDocumentEntity</c>: versión + integridad).
/// </summary>
public sealed record TenantBrandLogoVersion(
    Guid Id,
    Guid TenantId,
    int Version,
    string Status,
    string ContentType,
    string Filename,
    string StoragePath,
    string StorageSha256,
    int SizeBytes,
    int WidthPx,
    int HeightPx)
{
    public const string StatusActive = "active";
    public const string StatusSuperseded = "superseded";
}

/// <summary>Datos de una versión nueva del logotipo a persistir (el binario ya está en storage).</summary>
public sealed record NewBrandLogo(
    string ContentType,
    string Filename,
    string StoragePath,
    string StorageSha256,
    int SizeBytes,
    int WidthPx,
    int HeightPx);

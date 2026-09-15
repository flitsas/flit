namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Persistencia de la identidad de marca de una cabeza MARCA_BLANCA (HU #12412, ADR-0060 D1).
/// Implementación EF Core en <c>Flit.Infrastructure.Persistence.Repositories.TenantBrandingRepository</c>.
/// Ningún método filtra por <c>tenant_type</c>: el disparador de BD (<see cref="BrandingTenantNotMarcaBlancaException"/>)
/// es quien impide escribir sobre una cabeza que no sea MARCA_BLANCA (fail-closed en BD, AC2).
/// </summary>
public interface ITenantBrandingRepository
{
    /// <summary><c>null</c> si la cabeza nunca tuvo configuración inicial (AC1).</summary>
    Task<TenantBranding?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea la fila (primer <c>PUT</c> del SuperAdmin, AC1) o reemplaza el borrador de la existente
    /// (AC4: no afecta lo publicado). Audita <c>TenantBranding.draft</c> old/new en el mismo
    /// <c>SaveChanges</c>. Lanza <see cref="BrandingTenantNotMarcaBlancaException"/> si el disparador
    /// rechaza la escritura (AC2).
    /// </summary>
    Task<TenantBranding> UpsertDraftAsync(
        Guid tenantId,
        BrandingDraft draft,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copia <c>draft</c> → <c>published</c>, incrementa <c>published_version</c> y fija
    /// instante/autor (AC4). Audita <c>TenantBranding.published</c> old/new.
    /// </summary>
    Task<TenantBranding> PublishAsync(
        Guid tenantId,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Retiro lógico (AC6): fija <c>deleted_at</c>; el dato se conserva. Audita <c>TenantBranding.retired</c>.</summary>
    Task<TenantBranding> RetireAsync(
        Guid tenantId,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrega una versión nueva del logotipo, marcando la anterior <c>superseded</c> (AC7). Audita
    /// <c>TenantBrandLogo.logo</c> old/new. Lanza <see cref="BrandingTenantNotMarcaBlancaException"/>
    /// si el disparador rechaza la escritura.
    /// </summary>
    Task<TenantBrandLogoVersion> AddLogoVersionAsync(
        Guid tenantId,
        NewBrandLogo logo,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    Task<TenantBrandLogoVersion?> GetLogoVersionAsync(
        Guid tenantId,
        Guid logoId,
        CancellationToken cancellationToken = default);
}

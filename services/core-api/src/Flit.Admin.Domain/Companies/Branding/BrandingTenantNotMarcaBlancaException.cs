namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Traduce el <c>check_violation</c> (23514) del disparador compartido
/// <c>identity.trg_require_marca_blanca_head()</c> (<c>ck_tenant_brandings_marca_blanca</c> /
/// <c>ck_tenant_brand_logos_marca_blanca</c>) a un error de negocio identificable (HU #12412 AC2).
/// </summary>
public sealed class BrandingTenantNotMarcaBlancaException(Guid tenantId)
    : Exception($"El tenant {tenantId} no es una cabeza de grupo de tipo MARCA_BLANCA.")
{
    public Guid TenantId { get; } = tenantId;
}

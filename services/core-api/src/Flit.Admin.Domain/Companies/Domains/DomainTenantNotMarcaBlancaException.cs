namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Traduce el <c>check_violation</c> (23514) del disparador compartido
/// <c>identity.trg_require_marca_blanca_head()</c> (<c>ck_tenant_domains_marca_blanca</c>, DDL 116) a
/// un error de negocio identificable (HU #12416 AC2). Mismo patrón que
/// <c>Flit.Admin.Domain.Companies.Branding.BrandingTenantNotMarcaBlancaException</c>.
/// </summary>
public sealed class DomainTenantNotMarcaBlancaException(Guid tenantId)
    : Exception($"El tenant {tenantId} no es una cabeza de grupo de tipo MARCA_BLANCA.")
{
    public Guid TenantId { get; } = tenantId;
}

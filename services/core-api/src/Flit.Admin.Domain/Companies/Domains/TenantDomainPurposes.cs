namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Propósito de un dominio de red (<c>admin.tenant_domains.purpose</c>, contrato v1 §5, HU #12968):
/// <see cref="Hub"/> es el hub y login de la red; cualquier otro valor es el código de un producto.
/// </summary>
public static class TenantDomainPurposes
{
    public const string Hub = "HUB";
}

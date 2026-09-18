namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Vocabulario de estados del dominio de una red (HU #12416, ADR-0060 D1). Espejo, en la capa de
/// dominio, de <c>ck_tenant_domains_status</c> (DDL 116) — la capa de dominio no referencia
/// <c>Flit.Infrastructure</c>, así que no reutiliza <c>TenantDomainStatuses</c> de la entidad EF.
/// <c>pending → verified → active</c>; cualquiera puede pasar a <c>failed</c>. El ciclo real
/// (comprobación DNS, activación) lo gobierna #12425; aquí toda fila nace y permanece <c>pending</c>.
/// </summary>
public static class TenantDomainStatus
{
    public const string Pending = "pending";
    public const string Verified = "verified";
    public const string Active = "active";
    public const string Failed = "failed";
}

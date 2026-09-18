namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Vocabulario estable de <c>admin.tenant_domains.failure_reason</c> / <c>statusReason</c> del
/// contrato (HU #12425 AC2). Solo aplica cuando el estado es <see cref="TenantDomainStatus.Failed"/>
/// (<c>ck_tenant_domains_failed_requires_reason</c>, DDL 116). Parte del contrato público
/// (<c>contracts/openapi/core-api.v1.yaml</c>): no renombrar.
/// </summary>
public static class DomainStatusReasons
{
    /// <summary>No existe ningún registro TXT en <c>_flit-verify.&lt;host&gt;</c>.</summary>
    public const string TxtNotFound = "TXT_NOT_FOUND";

    /// <summary>Existe al menos un registro TXT en <c>_flit-verify.&lt;host&gt;</c> pero ninguno coincide con el token esperado.</summary>
    public const string TxtMismatch = "TXT_MISMATCH";

    /// <summary>La consulta DNS falló (timeout, servidor inalcanzable, respuesta malformada).</summary>
    public const string DnsError = "DNS_ERROR";
}

namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Vocabulario estable de códigos de error del dominio dedicado de la red (contrato
/// <c>contracts/openapi/core-api.v1.yaml</c>, HU #12416). Igual que
/// <c>Flit.Admin.Domain.Companies.Branding.BrandingErrors</c>: parte del contrato público, no renombrar.
/// </summary>
public static class DomainErrors
{
    public const string NotFound = "DOMAIN_NOT_FOUND";

    /// <summary>Formato inválido (RFC 1123) o IDN mal formado (falla la conversión a punycode). AC1.</summary>
    public const string HostInvalid = "DOMAIN_HOST_INVALID";

    /// <summary>Dominio de FLIT o del portal de organismos de tránsito. AC3.</summary>
    public const string HostReserved = "DOMAIN_HOST_RESERVED";

    /// <summary>El disparador de BD rechazó la escritura: la cabeza no es MARCA_BLANCA. AC2.</summary>
    public const string TenantNotMarcaBlanca = "DOMAIN_TENANT_NOT_MARCA_BLANCA";

    /// <summary>El host ya está registrado y vigente por OTRA red (<c>uq_tenant_domains_host</c>). AC1/AC2.</summary>
    public const string HostAlreadyRegistered = "DOMAIN_HOST_ALREADY_REGISTERED";

    /// <summary>La red YA tiene un dominio vigente (<c>uq_tenant_domains_tenant_id</c>) — defensa de carrera; el flujo normal lo evita con retiro+alta atómicos. AC2.</summary>
    public const string AlreadyRegisteredForTenant = "DOMAIN_ALREADY_REGISTERED_FOR_TENANT";

    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";

    /// <summary>Comprobación a demanda pedida antes de que venza <c>Domains:Verification:ManualCooldownSeconds</c> desde la última. HU #12425.</summary>
    public const string VerificationCooldown = "DOMAIN_VERIFICATION_COOLDOWN";

    /// <summary>El dominio aún no pasó a <c>verified</c>/<c>active</c>: no puede recibir la señal de certificado (#12426). HU #12425 AC3.</summary>
    public const string NotVerified = "DOMAIN_NOT_VERIFIED";
}

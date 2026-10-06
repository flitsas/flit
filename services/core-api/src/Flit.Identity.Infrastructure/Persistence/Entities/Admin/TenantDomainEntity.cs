namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Dominio propio de una red MARCA_BLANCA — <c>admin.tenant_domains</c> (HU #12416, Feature #12368,
/// ADR-0060 D1). Un dominio vigente por cabeza y propósito (<c>uq_tenant_domains_tenant_purpose</c>, HU #12968) y un
/// <see cref="Host"/> único en la plataforma (<c>uq_tenant_domains_host</c>), ambos parciales
/// <c>WHERE deleted_at IS NULL</c>: retirar conserva la fila y permite re-registrar. El motor exige
/// <c>tenant_type = MARCA_BLANCA</c> al insertar o cambiar <c>tenant_id</c>/<c>host</c>
/// (<c>tr_tenant_domains_marca_blanca</c>) y valida el formato del host (RFC 1123, minúsculas,
/// punycode). Solo resuelve red en <see cref="Status"/> <c>active</c> vía
/// <see cref="ActiveNetworkDomainView"/>. Tenant-scoped (RLS decorativo; el aislamiento real es el
/// <c>WHERE tenant_id</c> del repositorio, patrón <see cref="TenantBrandingEntity"/>).
/// </summary>
public sealed class TenantDomainEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Nombre de host ya normalizado por la aplicación: minúsculas, punycode, sin esquema/puerto/ruta.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// HU #12968: <c>HUB</c> (hub y login de la red) o el código del producto que sirve este host. Uno vigente
    /// por (empresa, propósito). Las operaciones actuales de registro, verificación y retiro son del <c>HUB</c>.
    /// </summary>
    public string Purpose { get; set; } = Flit.Admin.Domain.Companies.Domains.TenantDomainPurposes.Hub;

    /// <summary><c>pending</c> → <c>verified</c> → <c>active</c>; cualquiera → <c>failed</c>. Ver <see cref="TenantDomainStatuses"/>.</summary>
    public string Status { get; set; } = TenantDomainStatuses.Pending;

    /// <summary>Valor esperado en TXT <c>_flit-verify.&lt;host&gt;</c>. Único en la plataforma, nunca se reutiliza.</summary>
    public string VerificationToken { get; set; } = string.Empty;

    public DateTimeOffset? VerifiedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? FailedAt { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Señal de #12426; requisito del motor para pasar a <c>active</c>.</summary>
    public DateTimeOffset? CertificateIssuedAt { get; set; }

    public DateTimeOffset? CertificateExpiresAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>Siguiente comprobación programada (#12425); <c>null</c> = ninguna.</summary>
    public DateTimeOffset? NextCheckAt { get; set; }

    public int CheckAttempts { get; set; }

    public DateTimeOffset? GraceUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Retiro lógico del dominio (AC5): la fila se conserva y deja de resolver.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public long RowVersion { get; set; }
}

/// <summary>Vocabulario de <c>admin.tenant_domains.status</c> (<c>ck_tenant_domains_status</c>).</summary>
public static class TenantDomainStatuses
{
    public const string Pending = "pending";
    public const string Verified = "verified";
    public const string Active = "active";
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All = [Pending, Verified, Active, Failed];
}

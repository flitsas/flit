namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Dominio propio de una cabeza MARCA_BLANCA (HU #12416, ADR-0060 D1) — proyección de
/// <c>admin.tenant_domains</c>, UNA fila vigente por cabeza (<c>DeletedAt is null</c>; el repositorio
/// solo devuelve la vigente, la histórica queda en BD). El ciclo de comprobación/activación real
/// (<see cref="TenantDomainStatus.Verified"/>/<see cref="TenantDomainStatus.Active"/>,
/// <see cref="CertificateIssuedAt"/>, <see cref="NextCheckAt"/>) es HU #12425; aquí toda fila nace
/// <see cref="TenantDomainStatus.Pending"/> y así se mantiene salvo que otra HU la transicione.
/// </summary>
public sealed class TenantDomain
{
    public required Guid TenantId { get; init; }

    /// <summary>Ya normalizado: minúsculas, punycode, sin esquema/puerto/ruta (<see cref="HostNormalizer"/>).</summary>
    public required string Host { get; init; }

    public required string Status { get; init; }

    /// <summary>Motivo legible cuando <see cref="Status"/> es <see cref="TenantDomainStatus.Failed"/>.</summary>
    public string? StatusReason { get; init; }

    /// <summary>Valor esperado en el TXT de comprobación (#12425). Se expone para las instrucciones DNS (contrato §3).</summary>
    public required string VerificationToken { get; init; }

    public DateTimeOffset? VerifiedAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CertificateIssuedAt { get; init; }

    public DateTimeOffset? CertificateExpiresAt { get; init; }

    public DateTimeOffset? LastCheckedAt { get; init; }

    public DateTimeOffset? NextCheckAt { get; init; }

    /// <summary>Comprobaciones consecutivas sin éxito (HU #12425 AC2, backoff creciente); se reinicia al verificar.</summary>
    public required int CheckAttempts { get; init; }

    public DateTimeOffset? GraceUntil { get; init; }

    /// <summary>Última escritura sobre la fila (proyección de <c>updated_at</c>); usado como <c>statusChangedAt</c> del contrato.</summary>
    public required DateTimeOffset StatusChangedAt { get; init; }

    public required long RowVersion { get; init; }
}

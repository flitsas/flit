namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Persistencia del dominio dedicado de la red (HU #12416, ADR-0060 D1). Implementación EF Core en
/// <c>Flit.Infrastructure.Persistence.Repositories.TenantDomainRepository</c>. Ningún método filtra por
/// <c>tenant_type</c>: el disparador de BD (<see cref="DomainTenantNotMarcaBlancaException"/>) es quien
/// impide escribir sobre una cabeza que no sea MARCA_BLANCA (fail-closed en BD, AC2).
/// </summary>
public interface ITenantDomainRepository
{
    /// <summary><c>null</c> si la red no tiene dominio vigente (AC1). Solo la fila vigente (<c>deleted_at IS NULL</c>).</summary>
    Task<TenantDomain?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra (si la red no tenía dominio) o CAMBIA el dominio de la red (AC1, AC5): si ya existe uno
    /// vigente con OTRO host, lo retira (soft delete) y crea uno nuevo en <c>pending</c> con
    /// <paramref name="verificationToken"/>, en la MISMA transacción — audita <c>host</c> old/new. Si el
    /// host vigente ES el mismo, no reinicia el ciclo (idempotente). Lanza
    /// <see cref="DomainTenantNotMarcaBlancaException"/>, <see cref="DomainHostAlreadyRegisteredException"/>,
    /// <see cref="DomainAlreadyRegisteredForTenantException"/> o <see cref="DomainHostInvalidException"/>
    /// si el motor rechaza la escritura.
    /// </summary>
    Task<TenantDomain> RegisterOrReplaceAsync(
        Guid tenantId,
        string host,
        string verificationToken,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Retiro lógico (AC5): <c>null</c> si la red no tenía dominio vigente. Audita <c>host</c> → null.</summary>
    Task<TenantDomain?> RetireAsync(Guid tenantId, Guid? changedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// El único camino de resolución de red por host (ADR-0060 D2): lee
    /// <c>admin.v_active_network_domains</c> (solo <c>active</c>, vigente, cabeza MARCA_BLANCA activa).
    /// <c>null</c> = sin red (AC4). <paramref name="host"/> debe llegar ya normalizado.
    /// </summary>
    Task<Guid?> FindActiveHeadTenantIdAsync(string host, CancellationToken cancellationToken = default);

    /// <summary>Hosts activos de toda la plataforma (CORS del Gateway, consumido por #12417).</summary>
    Task<IReadOnlyList<string>> ListActiveHostsAsync(CancellationToken cancellationToken = default);
}

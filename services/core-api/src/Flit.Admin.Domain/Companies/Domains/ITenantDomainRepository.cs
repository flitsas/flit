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

    /// <summary>
    /// Hosts de cabezas MARCA_BLANCA activas y vigentes que necesitan certificado (#12426, poller ACME):
    /// <c>verified</c> sin <c>certificate_issued_at</c>, o <c>active</c> con
    /// <c>certificate_expires_at &lt;= renewBefore</c> (renovación próxima a vencer). Consumido por
    /// <c>GET /internal/domains/pending-certificate</c>.
    /// </summary>
    Task<IReadOnlyList<string>> ListPendingCertificateHostsAsync(DateTimeOffset renewBefore, CancellationToken cancellationToken = default);

    /// <summary>La fila vigente por host, sin importar estado (HU #12425 AC3, consumido por <c>PUT /internal/domains/{host}/certificate</c>). <c>null</c> si no existe o fue retirada.</summary>
    Task<TenantDomain?> GetByHostAsync(string host, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclama hasta <paramref name="batchSize"/> filas vencidas (<c>next_check_at &lt;= now</c>, HU #12425
    /// AC2, AC4, AC6) con <c>FOR UPDATE SKIP LOCKED</c> sobre <c>ix_tenant_domains_next_check</c> y les
    /// aplica un "lease" corto (adelanta <c>next_check_at</c>) para que otra réplica no las vuelva a
    /// tomar mientras esta procesa la comprobación DNS FUERA de la transacción de reclamo — si el
    /// proceso muere a medias, el lease expira y la fila vuelve a ser reclamable (autocorrectivo). Sin
    /// filas vencidas devuelve lista vacía (AC6, "el trabajo programado no hace nada").
    /// </summary>
    Task<IReadOnlyList<DomainCheckClaim>> ClaimDueForCheckAsync(int batchSize, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aplica el resultado de UNA comprobación DNS (a demanda o del job, HU #12425 AC2, AC4, AC5, AC7) y
    /// audita la transición (<c>EntityName=TenantDomain</c>, <c>FieldName=status</c>,
    /// <c>NewValue={"status","reason","changedBy"}</c>). Exactamente uno de <paramref name="changedByUserId"/>
    /// / <paramref name="changedByJob"/> identifica al autor (AC5); ninguno de los dos = sistema.
    /// <c>null</c> si <paramref name="tenantId"/> ya no tiene fila vigente (carrera con un retiro).
    /// </summary>
    Task<TenantDomain?> ApplyCheckOutcomeAsync(
        Guid tenantId,
        string newStatus,
        string? statusReason,
        DateTimeOffset? verifiedAt,
        DateTimeOffset? graceUntil,
        int checkAttempts,
        DateTimeOffset? nextCheckAt,
        DateTimeOffset now,
        Guid? changedByUserId,
        string? changedByJob,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aplica la señal de certificado emitido (#12426 → HU #12425 AC3): <paramref name="newStatus"/> ya
    /// viene decidido por <c>DomainStateMachine.DecideCertificate</c> (Application). <c>null</c> si
    /// <paramref name="host"/> no tiene fila vigente (404 del endpoint interno).
    /// </summary>
    Task<TenantDomain?> ApplyCertificateAsync(
        string host,
        string newStatus,
        DateTimeOffset? activatedAt,
        DateTimeOffset certificateIssuedAt,
        DateTimeOffset? certificateExpiresAt,
        string changedByJob,
        CancellationToken cancellationToken = default);
}

/// <summary>Fila reclamada por el job de comprobación (HU #12425), datos mínimos para decidir la transición sin recargar toda la entidad.</summary>
public sealed record DomainCheckClaim(Guid TenantId, string Host, string Status, DateTimeOffset? GraceUntil, int CheckAttempts);

using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Infrastructure.Domains;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del dominio dedicado de la red (HU #12416, ADR-0060 D1/D2) sobre
/// <c>admin.tenant_domains</c> / <c>admin.v_active_network_domains</c>. Sin <c>HasQueryFilter</c>
/// (convención vigente, delta-hechos #12): cada consulta filtra <c>tenant_id</c> explícitamente. La
/// auditoría legible (old/new) se escribe en <c>admin.tenant_config_audit_logs</c> en el MISMO
/// <c>SaveChanges</c> que la fila (patrón <c>TenantBrandingRepository</c>); el rastro técnico por
/// columna lo cubre el trigger <c>tr_tenant_domains_audit</c> de BD (DDL 116). El disparador
/// <c>identity.trg_require_marca_blanca_head()</c> rechaza con <c>check_violation</c> y
/// <c>ConstraintName = ck_tenant_domains_marca_blanca</c>; se traduce a
/// <see cref="DomainTenantNotMarcaBlancaException"/> (AC2).
/// </summary>
internal sealed class TenantDomainRepository : ITenantDomainRepository
{
    private readonly FlitDbContext _context;
    private readonly IAuditContextAccessor _auditContext;
    private readonly IMemoryCache? _cache;

    public TenantDomainRepository(FlitDbContext context, IAuditContextAccessor? auditContext = null, IMemoryCache? cache = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? NullAuditContextAccessor.Instance;
        _cache = cache;
    }

    public async Task<TenantDomain?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.TenantDomains
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task<TenantDomain> RegisterOrReplaceAsync(
        Guid tenantId,
        string host,
        string verificationToken,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationToken);

        var current = await _context.TenantDomains
            .Where(d => d.TenantId == tenantId && d.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // El host vigente NO cambia: idempotente, no reinicia el ciclo de comprobación (AC5).
        if (current is not null && string.Equals(current.Host, host, StringComparison.Ordinal))
        {
            return Map(current);
        }

        var now = DateTimeOffset.UtcNow;
        var created = new TenantDomainEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Host = host,
            Status = TenantDomainStatuses.Pending,
            VerificationToken = verificationToken,
            CreatedAt = now,
            CreatedBy = changedBy,
            UpdatedAt = now,
            UpdatedBy = changedBy,
        };
        _context.TenantDomains.Add(created);

        string? oldHost = null;
        if (current is not null)
        {
            // Retiro del dominio anterior EN LA MISMA transacción que el alta del nuevo: el índice
            // único parcial uq_tenant_domains_tenant_id (WHERE deleted_at IS NULL) solo cuenta la fila
            // vigente, así que esto no colisiona (AC5). Solo toca deleted_at/updated_at — el disparador
            // tr_tenant_domains_marca_blanca vigila tenant_id/host, no estas columnas.
            oldHost = current.Host;
            current.DeletedAt = now;
            current.DeletedBy = changedBy;
            current.UpdatedAt = now;
            current.UpdatedBy = changedBy;
        }

        AddAudit(tenantId, oldHost, host, current is null ? AuditVocabulary.Operations.Create : AuditVocabulary.Operations.Update, changedBy, now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsConstraint(ex, "ck_tenant_domains_marca_blanca"))
        {
            _context.ChangeTracker.Clear();
            throw new DomainTenantNotMarcaBlancaException(tenantId);
        }
        catch (DbUpdateException ex) when (IsConstraint(ex, "uq_tenant_domains_host"))
        {
            _context.ChangeTracker.Clear();
            throw new DomainHostAlreadyRegisteredException(host);
        }
        catch (DbUpdateException ex) when (IsConstraint(ex, "uq_tenant_domains_tenant_id"))
        {
            _context.ChangeTracker.Clear();
            throw new DomainAlreadyRegisteredForTenantException(tenantId);
        }
        catch (DbUpdateException ex) when (IsCheckViolation(ex))
        {
            _context.ChangeTracker.Clear();
            throw new DomainHostInvalidException(host);
        }

        await _context.Entry(created).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return Map(created);
    }

    public async Task<TenantDomain?> RetireAsync(Guid tenantId, Guid? changedBy, CancellationToken cancellationToken = default)
    {
        var current = await _context.TenantDomains
            .Where(d => d.TenantId == tenantId && d.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var oldHost = current.Host;
        current.DeletedAt = now;
        current.DeletedBy = changedBy;
        current.UpdatedAt = now;
        current.UpdatedBy = changedBy;

        AddAudit(tenantId, oldHost, null, AuditVocabulary.Operations.Update, changedBy, now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _context.Entry(current).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return Map(current);
    }

    public async Task<Guid?> FindActiveHeadTenantIdAsync(string host, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return await _context.ActiveNetworkDomains
            .AsNoTracking()
            .Where(d => d.Host == host)
            .Select(d => (Guid?)d.HeadTenantId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListActiveHostsAsync(CancellationToken cancellationToken = default) =>
        await _context.ActiveNetworkDomains
            .AsNoTracking()
            .Select(d => d.Host)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> ListPendingCertificateHostsAsync(DateTimeOffset renewBefore, CancellationToken cancellationToken = default)
    {
        var query =
            from d in _context.TenantDomains.AsNoTracking()
            join t in _context.Tenants.AsNoTracking() on d.TenantId equals t.Id
            where d.DeletedAt == null
                  && t.TenantType == "MARCA_BLANCA"
                  && t.IsGroupParent
                  && t.IsActive
                  && (
                      (d.Status == TenantDomainStatuses.Verified && d.CertificateIssuedAt == null)
                      || (d.Status == TenantDomainStatuses.Active && d.CertificateExpiresAt != null && d.CertificateExpiresAt <= renewBefore)
                  )
            select d.Host;

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TenantDomain?> GetByHostAsync(string host, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var entity = await _context.TenantDomains
            .AsNoTracking()
            .Where(d => d.Host == host && d.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<DomainCheckClaim>> ClaimDueForCheckAsync(
        int batchSize, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            return [];
        }

        // UPDATE ... RETURNING con SKIP LOCKED en una única sentencia (sin transacción manual): el
        // "lease" (adelantar next_check_at) es la propia reclamación — si el proceso muere antes de
        // aplicar el resultado, la fila vuelve a ser reclamable cuando el lease expira (HU #12425 AC2,
        // AC6). EF Core no expone FOR UPDATE SKIP LOCKED en LINQ; SQL parametrizado, sin concatenación.
        var connection = _context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE admin.tenant_domains
                SET next_check_at = @leased_until
                WHERE id IN (
                    SELECT id FROM admin.tenant_domains
                    WHERE next_check_at <= @now
                      AND deleted_at IS NULL
                      AND status IN ('pending', 'failed', 'active')
                    ORDER BY next_check_at
                    LIMIT @batch_size
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING tenant_id, host, status, grace_until, check_attempts
                """;

            AddParam(cmd, "leased_until", now + lease);
            AddParam(cmd, "now", now);
            AddParam(cmd, "batch_size", batchSize);

            var claims = new List<DomainCheckClaim>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                claims.Add(new DomainCheckClaim(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetInt32(4)));
            }

            return claims;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<TenantDomain?> ApplyCheckOutcomeAsync(
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
        CancellationToken cancellationToken = default)
    {
        var current = await _context.TenantDomains
            .Where(d => d.TenantId == tenantId && d.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return null;
        }

        var oldStatus = current.Status;
        current.Status = newStatus;
        current.FailureReason = newStatus == TenantDomainStatuses.Failed ? statusReason : null;
        current.FailedAt = newStatus == TenantDomainStatuses.Failed ? now : current.FailedAt;
        if (verifiedAt is not null)
        {
            current.VerifiedAt = verifiedAt;
        }
        current.GraceUntil = graceUntil;
        current.CheckAttempts = checkAttempts;
        current.LastCheckedAt = now;
        current.NextCheckAt = nextCheckAt;
        current.UpdatedAt = now;
        current.UpdatedBy = changedByUserId;

        AddStatusAudit(tenantId, oldStatus, newStatus, statusReason, changedByUserId, changedByJob, now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _context.Entry(current).ReloadAsync(cancellationToken).ConfigureAwait(false);

        InvalidateResolutionCache(current.Host, oldStatus, newStatus);

        return Map(current);
    }

    public async Task<TenantDomain?> ApplyCertificateAsync(
        string host,
        string newStatus,
        DateTimeOffset? activatedAt,
        DateTimeOffset certificateIssuedAt,
        DateTimeOffset? certificateExpiresAt,
        string changedByJob,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(changedByJob);

        var current = await _context.TenantDomains
            .Where(d => d.Host == host && d.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var oldStatus = current.Status;
        current.CertificateIssuedAt = certificateIssuedAt;
        current.CertificateExpiresAt = certificateExpiresAt;
        current.UpdatedAt = now;

        if (!string.Equals(oldStatus, newStatus, StringComparison.Ordinal))
        {
            current.Status = newStatus;
            current.ActivatedAt = activatedAt;
            AddStatusAudit(current.TenantId, oldStatus, newStatus, statusReason: null, changedByUserId: null, changedByJob: changedByJob, now);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _context.Entry(current).ReloadAsync(cancellationToken).ConfigureAwait(false);

        InvalidateResolutionCache(current.Host, oldStatus, current.Status);

        return Map(current);
    }

    /// <summary>Invalida la caché de resolución (60 s, <see cref="CachedTenantDomainResolver"/>) SIN esperar el TTL cuando el estado entra o sale de <c>active</c> (AC3).</summary>
    private void InvalidateResolutionCache(string host, string oldStatus, string newStatus)
    {
        if (_cache is null)
        {
            return;
        }

        var wasActive = oldStatus == TenantDomainStatuses.Active;
        var isActive = newStatus == TenantDomainStatuses.Active;
        if (wasActive == isActive)
        {
            return;
        }

        _cache.Remove(CachedTenantDomainResolver.ResolveCacheKey(host));
        _cache.Remove(CachedTenantDomainResolver.ActiveHostsCacheKey);
    }

    /// <summary>
    /// Auditoría legible de una transición de estado (HU #12425 AC5): <c>NewValue</c> lleva
    /// <c>{status, reason, changedBy}</c> compacto porque <c>TenantConfigAuditLog.ChangedBy</c> es
    /// <c>Guid?</c> (identidad de usuario) y no puede llevar el literal <c>"job:dns-verification"</c> —
    /// el autor "trabajo" queda igualmente trazado dentro del JSON, cumpliendo AC5 sin ampliar el
    /// esquema compartido de auditoría (decisión documentada en delta-hechos-post-adr.md).
    /// </summary>
    private void AddStatusAudit(Guid tenantId, string oldStatus, string newStatus, string? statusReason, Guid? changedByUserId, string? changedByJob, DateTimeOffset now)
    {
        var actor = changedByJob ?? changedByUserId?.ToString() ?? "system";
        var oldJson = JsonSerializer.Serialize(new { status = oldStatus });
        var newJson = JsonSerializer.Serialize(new { status = newStatus, reason = statusReason, changedBy = actor });

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = "TenantDomain",
            FieldName = "status",
            OldValue = oldJson,
            NewValue = newJson,
            ChangedAt = now,
            ChangedBy = changedByUserId,
            ClientIp = _auditContext.ClientIp,
            Operation = AuditVocabulary.Operations.Update,
            Result = AuditVocabulary.Results.Success,
            Module = AuditVocabulary.Modules.Companies,
            TargetEntityType = "TENANT_DOMAIN",
            TargetEntityId = tenantId,
        });
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private void AddAudit(Guid tenantId, string? oldHost, string? newHost, string operation, Guid? changedBy, DateTimeOffset now)
    {
        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = "TenantDomain",
            FieldName = "host",
            OldValue = oldHost,
            NewValue = newHost,
            ChangedAt = now,
            ChangedBy = changedBy,
            ClientIp = _auditContext.ClientIp,
            Operation = operation,
            Result = AuditVocabulary.Results.Success,
            Module = AuditVocabulary.Modules.Companies,
            TargetEntityType = "TENANT_DOMAIN",
            TargetEntityId = tenantId,
        });
    }

    private static bool IsConstraint(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException pg && pg.ConstraintName == constraintName;

    private static bool IsCheckViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.CheckViolation;

    private static TenantDomain Map(TenantDomainEntity entity) => new()
    {
        TenantId = entity.TenantId,
        Host = entity.Host,
        Status = entity.Status,
        StatusReason = entity.FailureReason,
        VerificationToken = entity.VerificationToken,
        VerifiedAt = entity.VerifiedAt,
        ActivatedAt = entity.ActivatedAt,
        CertificateIssuedAt = entity.CertificateIssuedAt,
        CertificateExpiresAt = entity.CertificateExpiresAt,
        LastCheckedAt = entity.LastCheckedAt,
        NextCheckAt = entity.NextCheckAt,
        CheckAttempts = entity.CheckAttempts,
        GraceUntil = entity.GraceUntil,
        StatusChangedAt = entity.UpdatedAt,
        RowVersion = entity.RowVersion,
    };
}

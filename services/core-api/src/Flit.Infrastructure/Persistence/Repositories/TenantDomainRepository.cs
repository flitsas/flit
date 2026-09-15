using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;
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

    public TenantDomainRepository(FlitDbContext context, IAuditContextAccessor? auditContext = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? NullAuditContextAccessor.Instance;
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
        GraceUntil = entity.GraceUntil,
        StatusChangedAt = entity.UpdatedAt,
        RowVersion = entity.RowVersion,
    };
}

using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class OtPrendaDocumentPolicyRepository : IOtPrendaDocumentPolicyRepository
{
    private const string EntityName = "tenant_transit_office_prenda_document_policies";
    private const string FieldName = "document_optional";

    private readonly FlitDbContext _context;
    private readonly IAuditContextAccessor _auditContext;

    public OtPrendaDocumentPolicyRepository(FlitDbContext context, IAuditContextAccessor auditContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public async Task<IReadOnlyList<OtPrendaDocumentPolicyItem>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TenantTransitOfficePrendaDocumentPolicies
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.DocumentOptional)
            .OrderBy(r => r.TransitOfficeId)
            .Select(r => new OtPrendaDocumentPolicyItem(r.TransitOfficeId, r.DocumentOptional))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsDocumentOptionalAtAsync(
        Guid tenantId,
        Guid transitOfficeId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        // Lectura del gate: puede ocurrir sin GUC de tenant (worker / SuperAdmin). Bypass RLS.
        return await ExecuteCrossTenantReadAsync(
            async () =>
            {
                var row = await _context.TenantTransitOfficePrendaDocumentPolicies
                    .AsNoTracking()
                    .Where(r =>
                        r.TenantId == tenantId
                        && r.TransitOfficeId == transitOfficeId
                        && r.DocumentOptional)
                    .Select(r => new { r.CreatedAt, r.UpdatedAt })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (row is null)
                    return false;

                var effectiveAt = row.UpdatedAt ?? row.CreatedAt;
                return effectiveAt <= asOf;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task SetAsync(
        Guid tenantId,
        Guid transitOfficeId,
        bool documentOptional,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            () => PersistSetAsync(
                tenantId, transitOfficeId, documentOptional, changedBy, correlationId, cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<OtPrendaDocumentPolicyCompanyItem>> ListCompaniesForOfficeAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                var grants = await _context.TenantTransitOfficeGrants
                    .AsNoTracking()
                    .Where(g => g.TransitOfficeId == transitOfficeId && g.IsEnabled)
                    .Select(g => g.TenantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (grants.Count == 0)
                    return (IReadOnlyList<OtPrendaDocumentPolicyCompanyItem>)[];

                var optionalByTenant = await _context.TenantTransitOfficePrendaDocumentPolicies
                    .AsNoTracking()
                    .Where(p =>
                        p.TransitOfficeId == transitOfficeId
                        && p.DocumentOptional
                        && grants.Contains(p.TenantId))
                    .Select(p => p.TenantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var optionalSet = optionalByTenant.ToHashSet();

                var tenants = await _context.Tenants
                    .AsNoTracking()
                    .Where(t => grants.Contains(t.Id))
                    .OrderBy(t => t.LegalName)
                    .Select(t => new { t.Id, t.LegalName })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<OtPrendaDocumentPolicyCompanyItem>)tenants
                    .Select(t => new OtPrendaDocumentPolicyCompanyItem(
                        t.Id, t.LegalName, optionalSet.Contains(t.Id)))
                    .ToList();
            },
            cancellationToken);

    private async Task ExecuteInTenantScopeAsync(
        Guid tenantId,
        Func<Task> persist,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    await persist().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            return;
        }

        await persist().ConfigureAwait(false);
    }

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
            return await action().ConfigureAwait(false);

        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database.ExecuteSqlRawAsync(
                "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);
                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private async Task PersistSetAsync(
        Guid tenantId,
        Guid transitOfficeId,
        bool documentOptional,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var existing = await _context.TenantTransitOfficePrendaDocumentPolicies
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.TransitOfficeId == transitOfficeId,
                cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var oldValue = existing is null ? null : JsonSerializer.Serialize(existing.DocumentOptional);

        // Check OFF (obligatorio) ⇒ eliminar fila (tabla dispersa).
        if (!documentOptional)
        {
            if (existing is null)
                return;

            _context.TenantTransitOfficePrendaDocumentPolicies.Remove(existing);
            _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EntityName = EntityName,
                FieldName = FieldName,
                OldValue = oldValue,
                NewValue = JsonSerializer.Serialize(false),
                ChangedAt = now,
                ChangedBy = changedBy,
                CorrelationId = correlationId,
                ClientIp = _auditContext.ClientIp,
                Operation = AuditVocabulary.Operations.Delete,
                Result = AuditVocabulary.Results.Success,
            });
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (existing is not null && existing.DocumentOptional)
            return;

        if (existing is null)
        {
            _context.TenantTransitOfficePrendaDocumentPolicies.Add(new TenantTransitOfficePrendaDocumentPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = transitOfficeId,
                DocumentOptional = true,
                CreatedAt = now,
                CreatedBy = changedBy,
            });
        }
        else
        {
            existing.DocumentOptional = true;
            existing.UpdatedAt = now;
            existing.UpdatedBy = changedBy;
        }

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = EntityName,
            FieldName = FieldName,
            OldValue = oldValue,
            NewValue = JsonSerializer.Serialize(true),
            ChangedAt = now,
            ChangedBy = changedBy,
            CorrelationId = correlationId,
            ClientIp = _auditContext.ClientIp,
            Operation = existing is null
                ? AuditVocabulary.Operations.Create
                : AuditVocabulary.Operations.Update,
            Result = AuditVocabulary.Results.Success,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

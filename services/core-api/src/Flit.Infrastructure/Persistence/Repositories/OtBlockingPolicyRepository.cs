using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de las políticas de bloqueo de preflight por OT de un tenant (FEATURE 05).
///
/// RLS: <c>admin.tenant_transit_office_blocking_policies</c> y
/// <c>admin.tenant_config_audit_logs</c> están aisladas por <c>app.current_tenant_id</c>.
/// Para la operación cross-tenant de SuperAdmin se fija ese GUC <em>dentro</em> de la transacción con
/// <c>set_config(..., is_local := true)</c> (parametrizado) antes de escribir. Clon de
/// <see cref="OtConsultationRestrictionRepository"/> salvo la columna de estado (<c>Blocks</c>).
/// </summary>
internal sealed class OtBlockingPolicyRepository : IOtBlockingPolicyRepository
{
    private const string EntityName = "tenant_transit_office_blocking_policies";

    private readonly FlitDbContext _context;
    private readonly IAuditContextAccessor _auditContext;

    public OtBlockingPolicyRepository(FlitDbContext context, IAuditContextAccessor auditContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public async Task<IReadOnlyList<OtBlockingPolicyItem>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TenantTransitOfficeBlockingPolicies
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.TransitOfficeId).ThenBy(r => r.Criterion)
            .Select(r => new OtBlockingPolicyItem(r.TransitOfficeId, r.Criterion, r.Blocks))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OtBlockingPolicyItem>> ListForOfficeAsync(
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TenantTransitOfficeBlockingPolicies
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.TransitOfficeId == transitOfficeId)
            .Select(r => new OtBlockingPolicyItem(r.TransitOfficeId, r.Criterion, r.Blocks))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SetAsync(
        Guid tenantId,
        Guid transitOfficeId,
        string criterion,
        bool blocks,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            () => PersistSetAsync(tenantId, transitOfficeId, criterion, blocks, changedBy, correlationId, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Ejecuta <paramref name="persist"/> bajo el contexto RLS del tenant. En proveedor relacional
    /// abre transacción + <c>set_config</c>; en InMemory delega directo.
    /// </summary>
    private async Task ExecuteInTenantScopeAsync(
        Guid tenantId,
        Func<Task> persist,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            // Transacción manual como unidad reintentable (el DbContext tiene EnableRetryOnFailure).
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    // RLS: habilita el acceso cross-tenant solo para esta transacción.
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    await persist().ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            return;
        }

        // Proveedor no relacional (InMemory): un solo SaveChanges atómico.
        await persist().ConfigureAwait(false);
    }

    private async Task PersistSetAsync(
        Guid tenantId,
        Guid transitOfficeId,
        string criterion,
        bool blocks,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var existing = await _context.TenantTransitOfficeBlockingPolicies
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId
                    && r.TransitOfficeId == transitOfficeId
                    && r.Criterion == criterion,
                cancellationToken)
            .ConfigureAwait(false);

        // Idempotencia: mismo estado deseado en ambos sentidos → no-op, sin auditoría.
        if (existing is not null && existing.Blocks == blocks)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var oldValue = existing is null ? null : JsonSerializer.Serialize(existing.Blocks);

        if (existing is null)
        {
            _context.TenantTransitOfficeBlockingPolicies.Add(new TenantTransitOfficeBlockingPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = transitOfficeId,
                Criterion = criterion,
                Blocks = blocks,
                CreatedAt = now,
                CreatedBy = changedBy,
            });
        }
        else
        {
            existing.Blocks = blocks;
            existing.UpdatedAt = now;
            existing.UpdatedBy = changedBy;
        }

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = EntityName,
            FieldName = criterion,
            OldValue = oldValue,
            NewValue = JsonSerializer.Serialize(blocks),
            ChangedAt = now,
            ChangedBy = changedBy,
            CorrelationId = correlationId,
            ClientIp = _auditContext.ClientIp,
            Operation = existing is null ? AuditVocabulary.Operations.Create : AuditVocabulary.Operations.Update,
            Result = AuditVocabulary.Results.Success,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

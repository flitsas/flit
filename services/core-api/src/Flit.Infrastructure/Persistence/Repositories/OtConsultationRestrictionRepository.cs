using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de las restricciones de consulta por OT de un tenant (HU #10759).
///
/// RLS: <c>admin.tenant_transit_office_consultation_restrictions</c> y
/// <c>admin.tenant_config_audit_logs</c> están aisladas por <c>app.current_tenant_id</c>.
/// Para la operación cross-tenant de SuperAdmin se fija ese GUC <em>dentro</em> de la
/// transacción con <c>set_config(..., is_local := true)</c> (parametrizado, sin concatenar
/// SQL) antes de escribir, y se revierte al cerrar la transacción.
///
/// Atomicidad: el upsert de la restricción y la inserción de su fila de auditoría ocurren en
/// una única transacción (todo o nada). En proveedores no relacionales (InMemory, tests) se
/// omite la transacción explícita y el GUC, preservando el comportamiento de un único
/// <c>SaveChanges</c>.
/// </summary>
internal sealed class OtConsultationRestrictionRepository : IOtConsultationRestrictionRepository
{
    private const string EntityName = "tenant_transit_office_consultation_restrictions";

    private readonly FlitDbContext _context;
    private readonly IAuditContextAccessor _auditContext;

    public OtConsultationRestrictionRepository(FlitDbContext context, IAuditContextAccessor auditContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public async Task<IReadOnlyList<OtConsultationRestrictionItem>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TenantTransitOfficeConsultationRestrictions
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.TransitOfficeId).ThenBy(r => r.ConsultationKind)
            .Select(r => new OtConsultationRestrictionItem(r.TransitOfficeId, r.ConsultationKind, r.Enabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListDisabledKindsAsync(
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TenantTransitOfficeConsultationRestrictions
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.TransitOfficeId == transitOfficeId && !r.Enabled)
            .Select(r => r.ConsultationKind)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OtConsultationRestrictionItem>> ListForOfficeAsync(
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TenantTransitOfficeConsultationRestrictions
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.TransitOfficeId == transitOfficeId)
            .Select(r => new OtConsultationRestrictionItem(r.TransitOfficeId, r.ConsultationKind, r.Enabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SetAsync(
        Guid tenantId,
        Guid transitOfficeId,
        string consultationKind,
        bool enabled,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            () => PersistSetAsync(tenantId, transitOfficeId, consultationKind, enabled, changedBy, correlationId, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Ejecuta <paramref name="persist"/> bajo el contexto RLS del tenant. En proveedor
    /// relacional abre transacción + <c>set_config</c>; en InMemory delega directo.
    /// </summary>
    private async Task ExecuteInTenantScopeAsync(
        Guid tenantId,
        Func<Task> persist,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            // La transacción manual debe ejecutarse como unidad reintentables porque
            // el DbContext tiene EnableRetryOnFailure (NpgsqlRetryingExecutionStrategy).
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
        string consultationKind,
        bool enabled,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var existing = await _context.TenantTransitOfficeConsultationRestrictions
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId
                    && r.TransitOfficeId == transitOfficeId
                    && r.ConsultationKind == consultationKind,
                cancellationToken)
            .ConfigureAwait(false);

        // Idempotencia (AC2): mismo estado deseado en ambos sentidos → no-op, sin auditoría.
        if (existing is not null && existing.Enabled == enabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var oldValue = existing is null ? null : JsonSerializer.Serialize(existing.Enabled);

        if (existing is null)
        {
            _context.TenantTransitOfficeConsultationRestrictions.Add(new TenantTransitOfficeConsultationRestriction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = transitOfficeId,
                ConsultationKind = consultationKind,
                Enabled = enabled,
                CreatedAt = now,
                CreatedBy = changedBy,
            });
        }
        else
        {
            existing.Enabled = enabled;
            existing.UpdatedAt = now;
            existing.UpdatedBy = changedBy;
        }

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = EntityName,
            FieldName = consultationKind,
            OldValue = oldValue,
            NewValue = JsonSerializer.Serialize(enabled),
            ChangedAt = now,
            ChangedBy = changedBy,
            CorrelationId = correlationId,
            // RNF01 (ADR-0024): create si la fila no existía, update si cambió de estado.
            ClientIp = _auditContext.ClientIp,
            Operation = existing is null ? AuditVocabulary.Operations.Create : AuditVocabulary.Operations.Update,
            Result = AuditVocabulary.Results.Success,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

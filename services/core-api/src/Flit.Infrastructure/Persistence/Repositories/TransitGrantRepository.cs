using System.Text.Json;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de los grants de organismos de tránsito por tenant
/// (HU #10192, RF13).
///
/// RLS: <c>admin.tenant_transit_office_grants</c> y <c>admin.tenant_config_audit_logs</c>
/// están aisladas por <c>app.current_tenant_id</c>. Para la operación cross-tenant de
/// SuperAdmin se fija ese GUC <em>dentro</em> de la transacción con
/// <c>set_config(..., is_local := true)</c> (parametrizado, sin concatenar SQL) antes
/// de escribir, y se revierte al cerrar la transacción.
///
/// Atomicidad: el alta/baja del grant y la inserción de su fila de auditoría ocurren
/// en una única transacción (todo o nada). En proveedores no relacionales (InMemory,
/// tests) se omite la transacción explícita y el GUC, preservando el comportamiento de
/// un único <c>SaveChanges</c>.
/// </summary>
internal sealed class TransitGrantRepository : ITransitGrantRepository
{
    private const string EntityName = "tenant_transit_office_grants";
    private const string FieldName = "transit_office_id";

    private readonly FlitDbContext _context;

    public TransitGrantRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<Guid>> ListEnabledOfficeIdsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TenantTransitOfficeGrants
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.IsEnabled)
            .OrderBy(g => g.CreatedAt)
            .Select(g => g.TransitOfficeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> AddGrantAsync(
        Guid tenantId,
        Guid transitOfficeId,
        Guid? createdBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            () => PersistAddAsync(tenantId, transitOfficeId, createdBy, correlationId, cancellationToken),
            cancellationToken);

    public Task<bool> RemoveGrantAsync(
        Guid tenantId,
        Guid transitOfficeId,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            () => PersistRemoveAsync(tenantId, transitOfficeId, changedBy, correlationId, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Ejecuta <paramref name="persist"/> bajo el contexto RLS del tenant. En proveedor
    /// relacional abre transacción + <c>set_config</c>; en InMemory delega directo.
    /// </summary>
    private async Task<bool> ExecuteInTenantScopeAsync(
        Guid tenantId,
        Func<Task<bool>> persist,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            // La transacción manual debe ejecutarse como unidad reintentables porque
            // el DbContext tiene EnableRetryOnFailure (NpgsqlRetryingExecutionStrategy).
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    // RLS: habilita el acceso cross-tenant solo para esta transacción.
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await persist().ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        // Proveedor no relacional (InMemory): un solo SaveChanges atómico.
        return await persist().ConfigureAwait(false);
    }

    private async Task<bool> PersistAddAsync(
        Guid tenantId,
        Guid transitOfficeId,
        Guid? createdBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        // Idempotencia (AC2): si ya existe el grant no se duplica fila ni auditoría.
        var alreadyExists = await _context.TenantTransitOfficeGrants
            .AnyAsync(g => g.TenantId == tenantId && g.TransitOfficeId == transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyExists)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        _context.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TransitOfficeId = transitOfficeId,
            IsEnabled = true,
            CreatedAt = now,
            CreatedBy = createdBy,
        });

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = EntityName,
            FieldName = FieldName,
            OldValue = null,
            NewValue = JsonSerializer.Serialize(transitOfficeId),
            ChangedAt = now,
            ChangedBy = createdBy,
            CorrelationId = correlationId,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> PersistRemoveAsync(
        Guid tenantId,
        Guid transitOfficeId,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var grant = await _context.TenantTransitOfficeGrants
            .FirstOrDefaultAsync(
                g => g.TenantId == tenantId && g.TransitOfficeId == transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        // AC3: 404 si el grant no existía.
        if (grant is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        _context.TenantTransitOfficeGrants.Remove(grant);

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = EntityName,
            FieldName = FieldName,
            OldValue = JsonSerializer.Serialize(transitOfficeId),
            NewValue = null,
            ChangedAt = now,
            ChangedBy = changedBy,
            CorrelationId = correlationId,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

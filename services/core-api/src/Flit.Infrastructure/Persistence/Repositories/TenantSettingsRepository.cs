using System.Text.Json;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de la configuración operativa del tenant (HU #10190).
///
/// RLS: <c>admin.tenant_operational_policies</c> y <c>admin.tenant_config_audit_logs</c>
/// están aisladas por <c>app.current_tenant_id</c>. Para la operación cross-tenant
/// de SuperAdmin se fija ese GUC <em>dentro</em> de la transacción con
/// <c>set_config(..., is_local := true)</c> (parametrizado, sin concatenar SQL)
/// antes de leer/escribir, y se revierte al cerrar la transacción.
///
/// Atomicidad (AC1): el upsert de la política y la inserción de las filas de
/// auditoría ocurren en una única transacción (todo o nada). En proveedores no
/// relacionales (InMemory, tests) se omite la transacción explícita y el GUC,
/// preservando el comportamiento de un único <c>SaveChanges</c>.
/// </summary>
internal sealed class TenantSettingsRepository : ITenantSettingsRepository
{
    private const string EntityName = "tenant_operational_policies";

    private readonly FlitDbContext _context;

    public TenantSettingsRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<TenantSettings?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.TenantOperationalPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task SaveAsync(
        TenantSettings settings,
        IReadOnlyList<TenantConfigChange> changes,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(changes);

        if (_context.Database.IsRelational())
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                // RLS: habilita el acceso cross-tenant solo para esta transacción.
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {settings.TenantId.ToString()}, true)",
                    cancellationToken).ConfigureAwait(false);

                await PersistAsync(settings, changes, changedBy, correlationId, cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        // Proveedor no relacional (InMemory): un solo SaveChanges atómico.
        await PersistAsync(settings, changes, changedBy, correlationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PersistAsync(
        TenantSettings settings,
        IReadOnlyList<TenantConfigChange> changes,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var policy = await _context.TenantOperationalPolicies
            .FirstOrDefaultAsync(p => p.TenantId == settings.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (policy is null)
        {
            policy = new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = settings.TenantId,
                CreatedAt = now,
                CreatedBy = changedBy,
            };
            _context.TenantOperationalPolicies.Add(policy);
        }
        else
        {
            policy.UpdatedAt = now;
            policy.UpdatedBy = changedBy;
        }

        ApplyTo(policy, settings);

        foreach (var change in changes)
        {
            _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = settings.TenantId,
                EntityName = EntityName,
                FieldName = change.FieldName,
                OldValue = change.OldValueJson,
                NewValue = change.NewValueJson,
                ChangedAt = now,
                ChangedBy = changedBy,
                CorrelationId = correlationId,
            });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyTo(TenantOperationalPolicy policy, TenantSettings settings)
    {
        policy.AllowInitialRegistration = settings.AllowInitialRegistration;
        policy.AllowMiscNewVehicles = settings.AllowMiscNewVehicles;
        policy.OnlyOwnVehicles = settings.OnlyOwnVehicles;
        policy.SignatureVaultEnabled = settings.SignatureVaultEnabled;
        policy.NotificationChannel = TenantSettingsCodes.ToDb(settings.NotificationChannel);
        policy.NotificationTarget = TenantSettingsCodes.ToDb(settings.NotificationTarget);
        policy.PaymentMethods = JsonSerializer.Serialize(settings.PaymentMethods);
    }

    private static TenantSettings Map(TenantOperationalPolicy entity) => new()
    {
        TenantId = entity.TenantId,
        AllowInitialRegistration = entity.AllowInitialRegistration,
        AllowMiscNewVehicles = entity.AllowMiscNewVehicles,
        OnlyOwnVehicles = entity.OnlyOwnVehicles,
        SignatureVaultEnabled = entity.SignatureVaultEnabled,
        NotificationChannel = TenantSettingsCodes.ParseChannelDb(entity.NotificationChannel),
        NotificationTarget = TenantSettingsCodes.ParseTargetDb(entity.NotificationTarget),
        PaymentMethods = DeserializePaymentMethods(entity.PaymentMethods),
    };

    private static List<string> DeserializePaymentMethods(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }
}

using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure.Auditing;

/// <summary>
/// Escribe filas en el rastro de auditoría administrativo/seguridad unificado (HU #10678,
/// extensión de RNF01/ADR-0024) sobre <c>admin.tenant_config_audit_logs</c>. Mismo patrón
/// que <see cref="AuditFailureWriter"/>: usa un <b>scope y DbContext propios</b> (vía
/// <see cref="IServiceScopeFactory"/>) con su propia transacción, de modo que la traza
/// sobreviva aunque la unidad de trabajo del cambio principal haga rollback. Registra tanto
/// éxito como fallo. Nunca propaga excepciones al llamante (best-effort).
/// </summary>
internal sealed class AdminAuditWriter : IAdminAuditWriter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AdminAuditWriter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task WriteAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

            var row = new TenantConfigAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = entry.TenantId,
                TenantType = entry.TenantType,
                EntityName = entry.EntityName,
                // Grano por operación (no por campo): no hay un field_name concreto en las
                // operaciones administrativas/seguridad, se usa la operación misma.
                FieldName = entry.Operation,
                OldValue = null,
                NewValue = null,
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = entry.ActorUserId,
                CorrelationId = null,
                ClientIp = entry.ClientIp,
                Result = entry.Result,
                Operation = entry.Operation,
                ErrorCode = entry.ErrorCode,
                Module = entry.Module,
                TargetEntityType = entry.TargetEntityType,
                TargetEntityId = entry.TargetEntityId,
                UserAgent = entry.UserAgent,
            };

            if (context.Database.IsRelational())
            {
                var strategy = context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    var transaction = await context.Database
                        .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                    await using (transaction.ConfigureAwait(false))
                    {
                        // RLS: la política tenant_isolation de tenant_config_audit_logs (HU
                        // #10678) admite filas sin tenant (tenant_id IS NULL) o del tenant
                        // actual. Cuando hay tenant se fija app.current_tenant_id; cuando no,
                        // se marca app.is_superadmin para no depender únicamente de la rama
                        // "tenant_id IS NULL" de la policy (defensa en profundidad).
                        if (entry.TenantId is { } tenantId)
                        {
                            await context.Database.ExecuteSqlInterpolatedAsync(
                                $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                                cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await context.Database.ExecuteSqlInterpolatedAsync(
                                $"SELECT set_config('app.is_superadmin', 'true', true)",
                                cancellationToken).ConfigureAwait(false);
                        }

                        context.TenantConfigAuditLogs.Add(row);
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

                return;
            }

            // Proveedor no relacional (InMemory): un solo SaveChanges en el scope propio.
            context.TenantConfigAuditLogs.Add(row);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Auditar es best-effort: si la propia escritura falla no debe romper la
            // respuesta al cliente ni ocultar el desenlace original. Se traga deliberadamente.
        }
    }
}

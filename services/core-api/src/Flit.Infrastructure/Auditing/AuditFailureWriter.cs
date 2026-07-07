using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure.Auditing;

/// <summary>
/// Escribe filas <c>result = failure</c> en <c>admin.tenant_config_audit_logs</c> (RNF01,
/// ADR-0024, sección 3). Clave: usa un <b>scope y DbContext propios</b> (vía
/// <see cref="IServiceScopeFactory"/>) con su propia transacción, de modo que la traza del
/// fallo persista aunque la unidad de trabajo del cambio principal haga rollback. Grano por
/// operación (no por campo). Nunca propaga excepciones al llamante: auditar el fallo no debe
/// convertirse en un segundo fallo que tape el desenlace real.
/// </summary>
internal sealed class AuditFailureWriter : IAuditFailureWriter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditFailureWriter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task WriteAsync(AuditFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

            var row = new TenantConfigAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = failure.TenantId,
                EntityName = failure.EntityName,
                // Grano por operación: no hay campo concreto, se marca la entidad completa.
                FieldName = failure.Operation,
                OldValue = null,
                NewValue = null,
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = failure.ChangedBy,
                CorrelationId = null,
                ClientIp = failure.ClientIp,
                Result = AuditVocabulary.Results.Failure,
                Operation = failure.Operation,
                ErrorCode = failure.ErrorCode,
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
                        // RLS: la escritura de tenant_config_audit_logs está aislada por tenant.
                        await context.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT set_config('app.current_tenant_id', {failure.TenantId.ToString()}, true)",
                            cancellationToken).ConfigureAwait(false);

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
            // Auditar el fallo es best-effort: si la propia escritura falla no debe romper la
            // respuesta al cliente ni ocultar el desenlace original. Se traga deliberadamente.
        }
    }
}

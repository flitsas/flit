using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Auditing;

/// <summary>
/// HU #12361 (Feature #12257) — escribe <c>tramites.network_access_audit</c>. Mismo patrón que
/// <see cref="AdminAuditWriter"/>: <b>scope y DbContext propios</b> (vía <see cref="IServiceScopeFactory"/>)
/// con su propia transacción, de modo que la traza no dependa de la unidad de trabajo de la petición.
/// Nunca propaga excepciones: un fallo queda como advertencia en el log (sin PII: solo recurso,
/// cabeza y número de hijos) y se devuelve <see langword="false"/> para que el llamante decida si la
/// respuesta sigue (best-effort) o se retiene (fail-closed en la descarga de documentos, hallazgo de
/// seguridad del PR #370).
/// </summary>
internal sealed partial class NetworkAccessAuditWriter : INetworkAccessAuditWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NetworkAccessAuditWriter> _logger;

    public NetworkAccessAuditWriter(IServiceScopeFactory scopeFactory, ILogger<NetworkAccessAuditWriter> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> WriteAsync(NetworkAccessAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // AC5 — sin hijos alcanzados no hay acceso consolidado que registrar. La cabeza nunca cuenta
        // como «alcanzada» (sus datos son propios): se retira aquí para que ningún llamante la cuele.
        var reached = entry.ReachedTenantIds
            .Where(t => t != Guid.Empty && t != entry.ActorTenantId)
            .Distinct()
            .ToArray();
        if (reached.Length == 0)
            return true;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

            // Sin Id explícito: lo genera la BD (DEFAULT uuidv7(), ValueGeneratedOnAdd en la
            // configuración) y así el orden natural de la clave sigue al tiempo, como el resto del esquema.
            var row = new Persistence.Entities.Tramites.NetworkAccessAuditEntry
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActorUserId = entry.ActorUserId,
                ActorTenantId = entry.ActorTenantId,
                ReachedTenantIds = reached,
                Resource = entry.Resource,
                Filters = entry.FiltersJson,
                ProcedureId = entry.ProcedureId,
                ProcedureTenantId = entry.ProcedureTenantId,
                AttachmentId = entry.AttachmentId,
                Result = entry.Result,
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
                        // RLS (defensa en profundidad): la policy admite a la cabeza actora.
                        await context.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT set_config('app.current_tenant_id', {entry.ActorTenantId.ToString()}, true)",
                            cancellationToken).ConfigureAwait(false);

                        context.NetworkAccessAuditEntries.Add(row);
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

                return true;
            }

            // Proveedor no relacional (InMemory): un solo SaveChanges en el scope propio.
            context.NetworkAccessAuditEntries.Add(row);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No se propaga: el llamante decide (best-effort o fail-closed) a partir del valor devuelto.
            WriteFailed(_logger, entry.Resource, entry.ActorTenantId, reached.Length, ex);
            return false;
        }
    }

    [LoggerMessage(
        EventId = 12361,
        Level = LogLevel.Warning,
        Message = "No se pudo registrar el acceso consolidado {Resource} de la cabeza {ActorTenantId} sobre {ReachedCount} hijo(s); el llamante decide si la respuesta se retiene.")]
    private static partial void WriteFailed(ILogger logger, string resource, Guid actorTenantId, int reachedCount, Exception ex);
}

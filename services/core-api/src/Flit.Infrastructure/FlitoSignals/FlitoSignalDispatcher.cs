using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Notificaciones.Webhooks;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.FlitoSignals;

/// <summary>
/// HU #13624 (Feature #13617, Épica #13614) — despacha los avisos de <c>integrations.flito_sync_signals</c> (HU #13622)
/// como webhooks firmados hacia FLITO. No hace el POST: en la misma transacción en que reclama el aviso deja el trabajo
/// <c>notificaciones.webhook.send</c> en la outbox del SDK (origen <c>flito</c>) y sella el aviso; core-notificaciones
/// lo entrega con sus reintentos y su <c>.dlq</c>. La clave de firma no sale de core-api.
/// <list type="bullet">
///   <item>Solo toma cambios con más de <see cref="FlitoSignalsOptions.StabilityLag"/> (la ventana del feed v3.1): antes,
///   FLITO leería el feed y todavía no encontraría el cambio.</item>
///   <item>Aplica la regla del feed: solo trámites radicados al menos una vez. Un aviso de un borrador se cierra sin
///   enviar (lo vuelve a abrir el siguiente cambio).</item>
///   <item>Reclamo <c>FOR NO KEY UPDATE SKIP LOCKED</c> y una transacción por aviso: seguro con varias réplicas.</item>
///   <item>Lectura entre compañías (ADR-0069): el rol de core-api es dueño de las tablas y no les aplica RLS;
///   <c>row_security = off</c> es la aserción de que siga así (mismo criterio que <c>ExternalSyncReadScope</c>).</item>
/// </list>
/// Sin bus (<see cref="IPlatformOutbox"/> sin registrar) o mal configurado no encola nada y no falla.
/// </summary>
internal sealed partial class FlitoSignalDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<FlitoSignalsOptions> options,
    ILogger<FlitoSignalDispatcher> logger) : BackgroundService
{
    public const string TipoAviso = "tramite.cambio";
    public const string Origen = "flito";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private bool _sinBusAvisado;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var o = options.CurrentValue;
            try
            {
                if (o.EsOperable(out _))
                {
                    await ProcesarPendientesAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogErrorCiclo(logger, ex);
            }

            try
            {
                await Task.Delay(o.PollInterval > TimeSpan.Zero ? o.PollInterval : TimeSpan.FromSeconds(1), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Despacha hasta <see cref="FlitoSignalsOptions.BatchSize"/> avisos maduros. Devuelve cuántos reclamó.</summary>
    internal async Task<int> ProcesarPendientesAsync(CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (!o.EsOperable(out _))
        {
            return 0;
        }

        var reclamados = 0;
        for (var i = 0; i < o.BatchSize && !ct.IsCancellationRequested; i++)
        {
            if (!await ProcesarSiguienteAsync(o, ct).ConfigureAwait(false))
            {
                break;
            }

            reclamados++;
        }

        return reclamados;
    }

    private async Task<bool> ProcesarSiguienteAsync(FlitoSignalsOptions o, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetService<IPlatformOutbox>();
        if (outbox is null)
        {
            if (!_sinBusAvisado)
            {
                _sinBusAvisado = true;
                LogSinBus(logger);
            }

            return false;
        }

        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", ct).ConfigureAwait(false);

            var aviso = await ReclamarAsync(db.Database.GetDbConnection(), tx.GetDbTransaction(), o.StabilityLag, ct).ConfigureAwait(false);
            if (aviso is null)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return false;
            }

            var enviar = aviso.Existe && !aviso.Migrado && aviso.Radicado;
            if (enviar)
            {
                Encolar(outbox, o, aviso);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE integrations.flito_sync_signals
                   SET pendiente_desde = NULL,
                       encolado_en = CASE WHEN {enviar} THEN clock_timestamp() ELSE encolado_en END
                 WHERE procedure_instance_id = {aviso.Id}
                """, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);

            if (enviar)
            {
                LogEncolado(logger, aviso.Id, aviso.SyncVersion, aviso.Eliminado);
            }
            else
            {
                LogCerradoSinEnviar(logger, aviso.Id);
            }

            return true;
        }).ConfigureAwait(false);
    }

    private static void Encolar(IPlatformOutbox outbox, FlitoSignalsOptions o, AvisoReclamado aviso)
    {
        var eventId = Guid.CreateVersion7();
        var cuerpo = JsonSerializer.Serialize(
            new AvisoFlito(eventId, TipoAviso, aviso.Id, aviso.SyncVersion, aviso.FechaUltimoCambio, aviso.Eliminado, aviso.TenantId),
            Json);

        outbox.EnqueueJob(
            TrabajoWebhook.Tipo,
            1,
            aviso.TenantId,
            new TrabajoWebhook(
                o.TargetUrl!,
                cuerpo,
                new Dictionary<string, string>
                {
                    ["X-Webhook-Signature"] = $"sha256={Firmar(cuerpo, o.SigningKey!)}",
                    ["X-Correlation-Id"] = eventId.ToString(),
                },
                Origen,
                TipoAviso));
    }

    /// <summary>HMAC-SHA256 del cuerpo exacto con la clave del ambiente, en hexadecimal en minúsculas.</summary>
    internal static string Firmar(string cuerpo, string clave) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(clave), Encoding.UTF8.GetBytes(cuerpo))).ToLowerInvariant();

    /// <summary>
    /// Reclama el aviso maduro más antiguo con el estado ACTUAL del trámite (versión, fecha, borrado) y la regla de
    /// radicado del feed. SQL crudo: EF no expone el locking clause. LEFT JOIN: un trámite que ya no existe cierra el aviso.
    /// </summary>
    private static async Task<AvisoReclamado?> ReclamarAsync(DbConnection conn, DbTransaction tx, TimeSpan lag, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT s.procedure_instance_id,
                   pi.id IS NOT NULL,
                   COALESCE(pi.is_migrated, false),
                   COALESCE(pi.tenant_id, s.tenant_id),
                   COALESCE(pi.sync_version, s.sync_version),
                   COALESCE(pi.sync_changed_at, s.sync_changed_at),
                   CASE WHEN pi.id IS NULL THEN s.eliminado ELSE pi.deleted_at IS NOT NULL END,
                   EXISTS (SELECT 1
                             FROM tramites.procedure_instance_status_history h
                            WHERE h.procedure_instance_id = s.procedure_instance_id
                              AND h.to_status IN ('preasignacion', 'entregado'))
              FROM integrations.flito_sync_signals s
              LEFT JOIN tramites.procedure_instances pi ON pi.id = s.procedure_instance_id
             WHERE s.pendiente_desde IS NOT NULL
               AND s.sync_changed_at <= clock_timestamp() - @lag
             ORDER BY s.sync_changed_at
             LIMIT 1
             FOR NO KEY UPDATE OF s SKIP LOCKED
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "lag";
        p.Value = lag;
        cmd.Parameters.Add(p);

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new AvisoReclamado(
            r.GetGuid(0),
            r.GetBoolean(1),
            r.GetBoolean(2),
            r.GetGuid(3),
            r.GetInt64(4),
            r.GetFieldValue<DateTimeOffset>(5),
            r.GetBoolean(6),
            r.GetBoolean(7));
    }

    private sealed record AvisoReclamado(
        Guid Id, bool Existe, bool Migrado, Guid TenantId, long SyncVersion, DateTimeOffset FechaUltimoCambio, bool Eliminado, bool Radicado);

    /// <summary>Cuerpo del aviso: el contrato acordado con FLITO (domain-events.v1.yaml, flito-webhook/tramite.cambio).</summary>
    internal sealed record AvisoFlito(
        Guid EventId, string Tipo, Guid Id, long SyncVersion, DateTimeOffset FechaUltimoCambio, bool Eliminado, Guid TenantId);

    [LoggerMessage(EventId = 13630, Level = LogLevel.Information, Message = "Aviso a FLITO encolado: trámite {ProcedureInstanceId}, versión {SyncVersion}, eliminado {Eliminado}.")]
    private static partial void LogEncolado(ILogger logger, Guid procedureInstanceId, long syncVersion, bool eliminado);

    [LoggerMessage(EventId = 13631, Level = LogLevel.Debug, Message = "Aviso a FLITO cerrado sin enviar (no radicado, migrado o inexistente): trámite {ProcedureInstanceId}.")]
    private static partial void LogCerradoSinEnviar(ILogger logger, Guid procedureInstanceId);

    [LoggerMessage(EventId = 13632, Level = LogLevel.Error, Message = "Avisos a FLITO encendidos pero sin bus de Trámites (IPlatformOutbox): no se enviará ninguno.")]
    private static partial void LogSinBus(ILogger logger);

    [LoggerMessage(EventId = 13633, Level = LogLevel.Error, Message = "Avisos a FLITO: error en el ciclo de despacho; se reintentará.")]
    private static partial void LogErrorCiclo(ILogger logger, Exception ex);
}

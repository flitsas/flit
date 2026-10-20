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
///   <item>Solo toma cambios ESTABLES con las dos condiciones del feed v3.1: más de
///   <see cref="FlitoSignalsOptions.StabilityLag"/> de antigüedad y ninguna transacción más antigua todavía abierta
///   (<c>sync_xact &lt; pg_snapshot_xmin</c>). Si no, FLITO leería el feed y todavía no encontraría el cambio.</item>
///   <item>Reclama en dos sentencias: primero bloquea el aviso y después lee el trámite con una foto NUEVA. Así nunca
///   decide con el estado de antes de un cambio confirmado durante el reclamo, y un cambio posterior espera el bloqueo
///   del aviso y lo vuelve a dejar pendiente.</item>
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
            var loteCompleto = false;
            try
            {
                if (o.EsOperable(out _))
                {
                    loteCompleto = await ProcesarPendientesAsync(stoppingToken).ConfigureAwait(false) >= o.BatchSize;
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

            if (loteCompleto)
            {
                continue; // hay atraso (p. ej. al volver a encender): sale sin esperar entre lotes
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

    /// <summary>
    /// Despacha hasta <see cref="FlitoSignalsOptions.BatchSize"/> avisos maduros. Devuelve cuántos despachó (enviados o
    /// cerrados sin enviar). Un aviso que
    /// falla o cuyo trámite todavía no es estable se aparta por el resto del ciclo: no frena a los demás.
    /// </summary>
    internal async Task<int> ProcesarPendientesAsync(CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (!o.EsOperable(out _))
        {
            return 0;
        }

        var apartados = new List<Guid>();
        var reclamados = 0;
        for (var i = 0; i < o.BatchSize && !ct.IsCancellationRequested; i++)
        {
            Guid? enCurso = null;
            try
            {
                var resultado = await ProcesarSiguienteAsync(o, apartados, id => enCurso = id, ct).ConfigureAwait(false);
                if (resultado == Resultado.SinPendientes)
                {
                    break;
                }

                if (resultado == Resultado.Aplazado)
                {
                    apartados.Add(enCurso!.Value);
                    continue;
                }

                reclamados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && enCurso is not null)
            {
                LogErrorAviso(logger, enCurso.Value, ex);
                apartados.Add(enCurso.Value);
            }
        }

        return reclamados;
    }

    private enum Resultado
    {
        SinPendientes,
        Despachado,
        Aplazado,
    }

    private async Task<Resultado> ProcesarSiguienteAsync(
        FlitoSignalsOptions o, IReadOnlyList<Guid> apartados, Action<Guid> alReclamar, CancellationToken ct)
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

            return Resultado.SinPendientes;
        }

        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", ct).ConfigureAwait(false);
            var conn = db.Database.GetDbConnection();
            var dbTx = tx.GetDbTransaction();

            var id = await ReclamarAsync(conn, dbTx, o.StabilityLag, apartados, ct).ConfigureAwait(false);
            if (id is null)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return Resultado.SinPendientes;
            }

            alReclamar(id.Value);
            var aviso = await LeerTramiteAsync(conn, dbTx, id.Value, ct).ConfigureAwait(false);
            if (aviso.Existe && !aviso.Estable)
            {
                // Lo cambió una transacción confirmada después de la foto del reclamo: aún no está en el feed. Queda
                // pendiente y sale en un ciclo próximo.
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return Resultado.Aplazado;
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

            return Resultado.Despachado;
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
    /// Bloquea el aviso maduro más antiguo. «Maduro» = las dos condiciones de estabilidad del feed. El LEFT JOIN solo
    /// filtra (no bloquea el trámite): los datos para decidir se leen después, en <see cref="LeerTramiteAsync"/>.
    /// SQL crudo: EF no expone el locking clause.
    /// </summary>
    private static async Task<Guid?> ReclamarAsync(
        DbConnection conn, DbTransaction tx, TimeSpan lag, IReadOnlyList<Guid> apartados, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT s.procedure_instance_id
              FROM integrations.flito_sync_signals s
              LEFT JOIN tramites.procedure_instances pi ON pi.id = s.procedure_instance_id
             WHERE s.pendiente_desde IS NOT NULL
               AND s.sync_changed_at <= clock_timestamp() - @lag
               AND (pi.id IS NULL OR COALESCE(pi.sync_xact, '0'::xid8) < pg_snapshot_xmin(pg_current_snapshot()))
               AND s.procedure_instance_id <> ALL(@apartados)
             ORDER BY s.sync_changed_at
             LIMIT 1
             FOR NO KEY UPDATE OF s SKIP LOCKED
            """;
        Parametro(cmd, "lag", lag);
        Parametro(cmd, "apartados", apartados.ToArray());

        var valor = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return valor is Guid id ? id : null;
    }

    /// <summary>
    /// Estado ACTUAL del trámite, en una sentencia aparte (foto nueva) con el aviso ya bloqueado, y la regla de radicado
    /// del feed. Si el trámite ya no existe se usan los datos del aviso (y no se envía).
    /// </summary>
    private static async Task<AvisoReclamado> LeerTramiteAsync(DbConnection conn, DbTransaction tx, Guid id, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT pi.id IS NOT NULL,
                   COALESCE(pi.is_migrated, false),
                   COALESCE(pi.sync_xact, '0'::xid8) < pg_snapshot_xmin(pg_current_snapshot()),
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
             WHERE s.procedure_instance_id = @id
            """;
        Parametro(cmd, "id", id);

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await r.ReadAsync(ct).ConfigureAwait(false);
        return new AvisoReclamado(
            id,
            r.GetBoolean(0),
            r.GetBoolean(1),
            r.GetBoolean(2),
            r.GetGuid(3),
            r.GetInt64(4),
            r.GetFieldValue<DateTimeOffset>(5),
            r.GetBoolean(6),
            r.GetBoolean(7));
    }

    private static void Parametro(DbCommand cmd, string nombre, object valor)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = nombre;
        p.Value = valor;
        cmd.Parameters.Add(p);
    }

    private sealed record AvisoReclamado(
        Guid Id, bool Existe, bool Migrado, bool Estable, Guid TenantId, long SyncVersion, DateTimeOffset FechaUltimoCambio,
        bool Eliminado, bool Radicado);

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

    [LoggerMessage(EventId = 13634, Level = LogLevel.Error, Message = "Avisos a FLITO: error al despachar el aviso del trámite {ProcedureInstanceId}; queda pendiente y se reintentará en el próximo ciclo.")]
    private static partial void LogErrorAviso(ILogger logger, Guid procedureInstanceId, Exception ex);
}

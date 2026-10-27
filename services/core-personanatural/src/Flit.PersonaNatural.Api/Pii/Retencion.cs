using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.PersonaNatural.Api.Pii;

/// <summary>
/// Retención de datos personales (HU #14037): un borrador abandonado más allá del plazo se anonimiza. Los datos personales
/// se vacían, la solicitud queda cancelada (ya no se puede reanudar), se revocan sus sesiones y queda la traza en
/// <c>acceso_pii_log</c>. Es idempotente: una solicitud ya cancelada no vuelve a entrar.
/// </summary>
public sealed class RetencionService(PersonaNaturalDb db, TimeProvider reloj, IEnumerable<Flit.PersonaNatural.Api.Solicitudes.ILimpiadorDeSolicitud> limpiadores)
{
    public const string ActorRetencion = "retencion";
    public const string AccionAnonimizada = "anonimizada_por_retencion";

    /// <summary>La política vive en la base y se relee en cada ciclo; si falta la fila, valen los valores por defecto del código.</summary>
    public async Task<ConfiguracionRetencion> LeerConfiguracionAsync(CancellationToken ct) =>
        await db.Set<ConfiguracionRetencion>().AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? new ConfiguracionRetencion();

    /// <summary>Anonimiza un lote de borradores abandonados y devuelve cuántos fueron.</summary>
    public async Task<int> AnonimizarAbandonadosAsync(CancellationToken ct)
    {
        var config = await LeerConfiguracionAsync(ct).ConfigureAwait(false);
        if (!config.Habilitado)
            return 0;

        // La transacción propia exige correr dentro de la estrategia de ejecución (reintentos de Npgsql).
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(ct2 => AnonimizarLoteAsync(config, ct2), ct).ConfigureAwait(false);
    }

    private async Task<int> AnonimizarLoteAsync(ConfiguracionRetencion config, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var limite = ahora.AddDays(-config.DiasRetencionBorrador);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // SKIP LOCKED: con varias réplicas, cada una toma lotes distintos y ninguna espera a la otra.
        var sql = $"""
            SELECT id AS "Value" FROM {PersonaNaturalDb.Schema}.solicitud_tramite
            WHERE estado = @estado AND updated_at < @limite
            ORDER BY updated_at LIMIT @lote FOR UPDATE SKIP LOCKED
            """;
        var ids = await db.Database.SqlQueryRaw<Guid>(sql,
                new NpgsqlParameter("estado", EstadoSolicitud.Borrador), new NpgsqlParameter("limite", limite), new NpgsqlParameter("lote", config.Lote))
            .ToListAsync(ct).ConfigureAwait(false);
        if (ids.Count == 0)
            return 0;

        await db.Set<PersonaSolicitante>().Where(p => ids.Contains(p.SolicitudId)).ExecuteUpdateAsync(set => set
            .SetProperty(p => p.DocumentoCifrado, _ => Array.Empty<byte>())
            .SetProperty(p => p.DocumentoHash, _ => Array.Empty<byte>())
            .SetProperty(p => p.NombreCifrado, _ => (byte[]?)null)
            .SetProperty(p => p.CorreoCifrado, _ => (byte[]?)null)
            .SetProperty(p => p.CelularCifrado, _ => (byte[]?)null)
            .SetProperty(p => p.DireccionCifrada, _ => (byte[]?)null)
            .SetProperty(p => p.UpdatedAt, _ => ahora), ct).ConfigureAwait(false);
        await db.Set<SesionTramite>().Where(s => ids.Contains(s.SolicitudId)).ExecuteUpdateAsync(set => set.SetProperty(s => s.Revocada, true), ct).ConfigureAwait(false);
        await db.Set<SolicitudTramite>().Where(s => ids.Contains(s.Id)).ExecuteUpdateAsync(set => set
            .SetProperty(s => s.Estado, EstadoSolicitud.Cancelada)
            .SetProperty(s => s.CedulaSolicitanteHash, _ => Array.Empty<byte>())
            .SetProperty(s => s.FieldValues, "{}")
            .SetProperty(s => s.Datos, "{}")
            .SetProperty(s => s.UpdatedAt, _ => ahora), ct).ConfigureAwait(false);

        // Los archivos que subió el ciudadano (cédulas, certificados) también son datos personales: salen del almacenamiento y de la base.
        // Si el almacenamiento falla, el borrador igual se anonimiza y las referencias se borran; el objeto huérfano lo recupera su limpieza.
        foreach (var id in ids)
        {
            try
            {
                foreach (var limpiador in limpiadores)
                    await limpiador.LimpiarAsync(id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not Npgsql.NpgsqlException)
            {
                // se sigue con el siguiente borrador
            }
        }

        await db.Set<Adjunto>().Where(a => ids.Contains(a.SolicitudId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        db.AddRange(ids.Select(id => new AccesoPiiLog { Actor = ActorRetencion, Accion = AccionAnonimizada, SolicitudId = id, OcurrioEn = ahora }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return ids.Count;
    }
}

/// <summary>Job periódico de retención, in-process (estándar de jobs de FLIT): relee su cadencia de la base en cada ciclo y un fallo no lo tumba.</summary>
public sealed partial class RetencionJob(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RetencionJob> logger) : BackgroundService
{
    public const string ClaveArranqueSegundos = "Retencion:ArranqueSegundos";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var arranque = configuration.GetValue(ClaveArranqueSegundos, 60);
        await Task.Delay(TimeSpan.FromSeconds(arranque), stoppingToken).ConfigureAwait(false);

        var intervalo = TimeSpan.FromHours(24);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var retencion = scope.ServiceProvider.GetRequiredService<RetencionService>();
                intervalo = TimeSpan.FromMinutes((await retencion.LeerConfiguracionAsync(stoppingToken).ConfigureAwait(false)).IntervaloMinutos);
                var total = await retencion.AnonimizarAbandonadosAsync(stoppingToken).ConfigureAwait(false);
                if (total > 0)
                    CicloTerminado(logger, total);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                CicloFallido(logger, ex);
            }

            await Task.Delay(intervalo, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retención: {Total} borradores abandonados anonimizados")]
    private static partial void CicloTerminado(ILogger logger, int total);

    [LoggerMessage(Level = LogLevel.Error, Message = "Retención: el ciclo falló y se reintenta en el siguiente")]
    private static partial void CicloFallido(ILogger logger, Exception ex);
}

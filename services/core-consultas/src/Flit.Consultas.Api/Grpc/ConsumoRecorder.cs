using System.Diagnostics;
using Flit.Api.Identity;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Consultas.Api.Grpc;

/// <summary>
/// Mide cada consulta (HU #13345): una fila en <c>consultas.consumo</c> al terminar, salga bien o mal, y el evento
/// <see cref="EventoConsumo"/> en la misma transacción (HU #13352). Si guardar la medición falla, la consulta no
/// falla: se registra en el log y se devuelve la respuesta.
/// </summary>
internal sealed class ConsumoRecorder(ConsultasDb db, IPlatformOutbox outbox, TimeProvider time, ILogger<ConsumoRecorder> logger)
{
    public Task<T> MedirAsync<T>(ServerCallContext context, string fuente, Func<Task<T>> consulta, Func<T, (string Proveedor, string Resultado, bool DesdeCache)> resumen)
    {
        ArgumentNullException.ThrowIfNull(context);
        return MedirAsync(PlatformServiceCaller.From(context), fuente, consulta, resumen, context.CancellationToken);
    }

    /// <summary>
    /// La misma medición con un llamador explícito (HU #13876, ADR-0075): el consumidor del bus pasa el del sobre
    /// (<c>svc-&lt;producer&gt;</c> y la empresa del <c>tenantId</c>). Una consulta cancelada por <paramref name="ct"/> no se mide,
    /// igual que una llamada gRPC cancelada.
    /// </summary>
    public async Task<T> MedirAsync<T>(
        PlatformServiceCaller caller, string fuente, Func<Task<T>> consulta, Func<T, (string Proveedor, string Resultado, bool DesdeCache)> resumen, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(consulta);
        ArgumentNullException.ThrowIfNull(resumen);
        var reloj = Stopwatch.StartNew();
        try
        {
            var respuesta = await consulta().ConfigureAwait(false);
            var (proveedor, resultado, desdeCache) = resumen(respuesta);
            await GuardarAsync(caller, fuente, proveedor, resultado, desdeCache, reloj.Elapsed).ConfigureAwait(false);
            return respuesta;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await GuardarAsync(caller, fuente, string.Empty, "error", false, reloj.Elapsed).ConfigureAwait(false);
            throw;
        }
    }

    public const string EventoConsumo = "consultas.consulta.realizada";

    public static string Resultado(Semaforo semaforo) => semaforo switch
    {
        Semaforo.Verde => "verde",
        Semaforo.Amarillo => "amarillo",
        Semaforo.Rojo => "rojo",
        _ => "sin_semaforo",
    };

    /// <summary><c>svc-tramites</c> → <c>tramites</c>.</summary>
    internal static string Producto(string clientId) =>
        clientId.StartsWith(OidcDefaults.ServiceClientPrefix, StringComparison.Ordinal) ? clientId[OidcDefaults.ServiceClientPrefix.Length..] : clientId;

    /// <summary>
    /// Guarda la fila y su evento. En el camino gRPC en línea (sin transacción) limpia el <c>DbContext</c> antes, como siempre.
    /// Dentro de una transacción (el consumidor del bus, HU #13876) no toca lo que ya está en el <c>DbContext</c> y mide en un
    /// savepoint: si Postgres rechaza la fila, se vuelve al savepoint (la transacción sigue viva) y se sueltan solo las dos
    /// entradas de la medición, para que el <c>SaveChanges</c> del consumidor publique el resultado igual.
    /// </summary>
    private async Task GuardarAsync(PlatformServiceCaller caller, string fuente, string proveedor, string resultado, bool desdeCache, TimeSpan latencia)
    {
        var transaccion = db.Database.CurrentTransaction;
        if (transaccion is null)
            db.ChangeTracker.Clear();

        var latenciaMs = (int)Math.Min(int.MaxValue, latencia.TotalMilliseconds);
        var fila = new ConsumoConsulta
        {
            Id = Guid.CreateVersion7(),
            TenantId = caller.TenantId,
            Producto = Producto(caller.ClientId),
            Fuente = fuente,
            Proveedor = proveedor,
            Resultado = resultado,
            DesdeCache = desdeCache,
            LatenciaMs = latenciaMs,
            OcurridoEn = time.GetUtcNow(),
        };
        var guardoSavepoint = false;
        EventEnvelope? evento = null;
        try
        {
            if (transaccion is not null)
            {
                await transaccion.CreateSavepointAsync(Savepoint, CancellationToken.None).ConfigureAwait(false);
                guardoSavepoint = true;
            }

            db.Consumos.Add(fila);
            evento = outbox.Enqueue(EventoConsumo, 1, caller.TenantId, new
            {
                producto = Producto(caller.ClientId),
                fuente,
                proveedor,
                resultado,
                desdeCache,
                latenciaMs,
            });
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            if (guardoSavepoint)
                await transaccion!.ReleaseSavepointAsync(Savepoint, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // La medición nunca tumba la consulta.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ConsumoLog.NoGuardado(logger, fuente, ex);
            await DeshacerAsync(transaccion, guardoSavepoint, fila, evento).ConfigureAwait(false);
        }
    }

    private const string Savepoint = "medicion_consumo";

    private async Task DeshacerAsync(IDbContextTransaction? transaccion, bool guardoSavepoint, ConsumoConsulta fila, EventEnvelope? evento)
    {
        db.Entry(fila).State = EntityState.Detached;
        if (evento is not null
            && db.ChangeTracker.Entries<OutboxMessage>().FirstOrDefault(e => e.Entity.Id == evento.EventId) is { } entrada)
            entrada.State = EntityState.Detached;
        if (!guardoSavepoint)
            return;
        try
        {
            await transaccion!.RollbackToSavepointAsync(Savepoint, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Sin conexión no hay savepoint que deshacer: el consumidor fallará al confirmar, como cualquier caída de su base.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ConsumoLog.SavepointNoDeshecho(logger, fuente: fila.Fuente, ex);
        }
    }
}

internal static partial class ConsumoLog
{
    [LoggerMessage(EventId = 7401, Level = LogLevel.Error, Message = "No se pudo guardar el consumo de una consulta de {Fuente}")]
    public static partial void NoGuardado(ILogger logger, string fuente, Exception ex);

    [LoggerMessage(EventId = 7402, Level = LogLevel.Error, Message = "No se pudo volver al savepoint de la medición de {Fuente}")]
    public static partial void SavepointNoDeshecho(ILogger logger, string fuente, Exception ex);
}

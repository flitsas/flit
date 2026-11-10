using Flit.PersonaNatural.Api.Caja;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>
/// Radicación diferida de lo cobrado en caja (Feature #14453, decisión del PO 2026-11-10). Un cobro con medio <c>caja</c> NO se
/// radica al pagar: la solicitud queda <c>pagada</c> y se radica cuando vence <see cref="CajaOptions.VentanaAnulacionMinutos"/>
/// contados desde <c>paid_at</c>. En esa ventana el administrador del organismo puede anular el cobro. Un total $0
/// (<c>sin_cobro</c>) se radica de inmediato. Es idempotente y seguro con varias réplicas: <see cref="RadicacionService"/> bloquea
/// la fila de la solicitud y una segunda radicación con la misma llave devuelve el resultado de la primera.
/// </summary>
public sealed class RadicacionDePagadasService(
    PersonaNaturalDb db, RadicacionService radicacion, IOptions<CajaOptions> opciones, TimeProvider reloj, ILogger<RadicacionDePagadasService> log)
{
    private readonly CajaOptions _o = opciones.Value;

    public TimeSpan Ventana => _o.Ventana;

    private TimeSpan ConGracia(TimeSpan ventana) => ventana > TimeSpan.Zero ? ventana + _o.Gracia : ventana;

    /// <summary>La ventana de un cobro en caja sigue abierta: aún se puede anular y todavía no se radica.</summary>
    public static bool EnVentana(string? medio, DateTimeOffset? pagadoEn, DateTimeOffset ahora, TimeSpan ventana) =>
        medio == MedioDePago.Caja && pagadoEn is { } pagado && ventana > TimeSpan.Zero && ahora < pagado + ventana;

    /// <summary>Instante en que se radica un cobro en caja; <c>null</c> si no hay ventana (sin_cobro o ventana en cero).</summary>
    public static DateTimeOffset? RadicaEn(string? medio, DateTimeOffset? pagadoEn, TimeSpan ventana) =>
        medio == MedioDePago.Caja && pagadoEn is { } pagado && ventana > TimeSpan.Zero ? pagado + ventana : null;

    /// <summary>
    /// Radica la solicitud <c>pagada</c> si su ventana ya venció (o no tiene). Devuelve <c>true</c> si quedó en cola. Lo usan la
    /// consulta del estado de pago y el cierre del punto. Con la ventana abierta no hace nada.
    /// </summary>
    public async Task<bool> IntentarRadicarSiVencioAsync(Guid solicitudId, CancellationToken ct)
    {
        var cobro = await db.Set<CobroSolicitud>().AsNoTracking()
            .Where(c => c.SolicitudId == solicitudId && c.Estado == EstadoCobro.Pagado)
            .Select(c => new { c.Medio, c.PagadoEn }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (cobro is not null && EnVentana(cobro.Medio, cobro.PagadoEn, reloj.GetUtcNow(), ConGracia(_o.Ventana)))
            return false;
        return await radicacion.IntentarRadicarPagadaAsync(solicitudId, ct).ConfigureAwait(false);
    }

    /// <summary>Radica un lote de solicitudes pagadas cuya ventana venció (o sin ventana). Devuelve cuántas quedaron en cola.</summary>
    public async Task<int> RadicarVencidasAsync(CancellationToken ct)
    {
        // La anulación usa el límite exacto de la ventana; la radicación espera además la gracia, así en el borde gana la anulación.
        var limite = reloj.GetUtcNow() - ConGracia(_o.Ventana);
        var ids = await (
            from c in db.Set<CobroSolicitud>().AsNoTracking()
            join s in db.Set<SolicitudTramite>().AsNoTracking() on c.SolicitudId equals s.Id
            where c.Estado == EstadoCobro.Pagado && s.Estado == EstadoSolicitud.Pagada && (c.Medio != MedioDePago.Caja || c.PagadoEn <= limite)
            orderby c.PagadoEn
            select s.Id).Take(Math.Max(1, _o.RadicacionLote)).ToListAsync(ct).ConfigureAwait(false);

        var radicadas = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (await radicacion.IntentarRadicarPagadaAsync(id, ct).ConfigureAwait(false))
                radicadas++;
        }

        if (radicadas > 0)
            LogsDeRadicacionDiferida.Radicadas(log, radicadas);
        return radicadas;
    }
}

/// <summary>Trabajo de fondo que radica los cobros en caja cuya ventana de anulación venció.</summary>
public sealed class RadicacionDePagadasJob(IServiceScopeFactory scopes, IOptions<CajaOptions> opciones, ILogger<RadicacionDePagadasJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = opciones.Value;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, o.RadicacionArranqueSegundos)), stoppingToken).ConfigureAwait(false);
            var intervalo = TimeSpan.FromSeconds(Math.Max(1, o.RadicacionIntervaloSegundos));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<RadicacionDePagadasService>().RadicarVencidasAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogsDeRadicacionDiferida.CicloFallido(log, ex);
                }

                await Task.Delay(intervalo, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

internal static partial class LogsDeRadicacionDiferida
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Radicación diferida: {Total} cobros en caja con la ventana vencida pasaron a la cola")]
    public static partial void Radicadas(ILogger logger, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Radicación diferida: el ciclo falló y se reintenta")]
    public static partial void CicloFallido(ILogger logger, Exception ex);
}

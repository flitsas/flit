using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Notificaciones;

/// <summary>
/// Purga física de la campana a los <see cref="OpcionesNotificaciones.RetencionDias"/> días (HU #14563 B8, schema §3.5, ADR-0086
/// excepción 1 y 5). Es la quinta escritura entre empresas del servicio: SQL constante y parametrizado, en bloques, que no
/// devuelve filas y solo toca <c>diagnostico.notificaciones</c>. Figura en la lista blanca de <c>TomaEntreEmpresasSoloEnColaTests</c>.
/// </summary>
public sealed class PurgaNotificaciones(DiagnosticoDb db, IOptions<OpcionesNotificaciones> opciones, TimeProvider reloj)
{
    internal const string SqlBorrado =
        "DELETE FROM diagnostico.notificaciones WHERE id IN " +
        "(SELECT id FROM diagnostico.notificaciones WHERE creada_en < @corte ORDER BY creada_en LIMIT @lote)";

    /// <summary>Borra en bloques las notificaciones anteriores al corte; devuelve cuántas borró.</summary>
    public async Task<int> PurgarAsync(CancellationToken ct)
    {
        var o = opciones.Value;
        var dias = o.RetencionDias > 0 ? o.RetencionDias : 90;
        var lote = o.TamanoLote > 0 ? o.TamanoLote : 500;
        var corte = reloj.GetUtcNow().AddDays(-dias);
        var total = 0;
        int borradas;
        do
        {
            borradas = await db.Database.ExecuteSqlRawAsync(
                SqlBorrado,
                [new NpgsqlParameter("corte", corte), new NpgsqlParameter("lote", lote)],
                ct).ConfigureAwait(false);
            total += borradas;
        }
        while (borradas == lote);
        return total;
    }
}

/// <summary>Corre <see cref="PurgaNotificaciones"/> cada <see cref="OpcionesNotificaciones.IntervaloPurga"/> (por defecto, diario).</summary>
internal sealed partial class TrabajadorPurgaNotificaciones(
    IServiceScopeFactory scopes, IOptions<OpcionesNotificaciones> opciones, TimeProvider reloj, ILogger<TrabajadorPurgaNotificaciones> log) : BackgroundService
{
    public const int EventoPurgaFallida = 145631;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = opciones.Value.IntervaloPurga > TimeSpan.Zero ? opciones.Value.IntervaloPurga : TimeSpan.FromHours(24);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var borradas = await scope.ServiceProvider.GetRequiredService<PurgaNotificaciones>().PurgarAsync(stoppingToken).ConfigureAwait(false);
                Purgadas(borradas);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Una falla de la base no tumba el servicio: se reintenta en la siguiente vuelta.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                PurgaFallida(ex);
            }
        }
        while (await Esperar(intervalo, stoppingToken).ConfigureAwait(false));
    }

    private async Task<bool> Esperar(TimeSpan intervalo, CancellationToken ct)
    {
        try
        {
            await Task.Delay(intervalo, reloj, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(EventId = EventoPurgaFallida + 1, Level = LogLevel.Information, Message = "Purga de notificaciones: {Borradas} filas.")]
    private partial void Purgadas(int borradas);

    [LoggerMessage(EventId = EventoPurgaFallida, Level = LogLevel.Warning, Message = "La purga de notificaciones falló; se reintenta en la siguiente vuelta.")]
    private partial void PurgaFallida(Exception ex);
}

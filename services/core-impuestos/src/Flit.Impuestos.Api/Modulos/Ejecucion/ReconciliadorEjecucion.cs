using System.Data.Common;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>Lo que hizo una vuelta del reconciliador.</summary>
/// <param name="Empresas">Compañías revisadas.</param>
/// <param name="Reprogramados">Reintentos vencidos que volvieron a <c>PENDIENTE</c>.</param>
/// <param name="Republicados">Ítems en vuelo cuyo mensaje se volvió a publicar.</param>
/// <param name="Liberados">Ítems que recibieron crédito en la vuelta.</param>
public sealed record VueltaEjecucion(int Empresas, int Reprogramados, int Republicados, int Liberados);

/// <summary>
/// Reconciliador de la ejecución (plan §1.4, DA-5; forma del reconciliador de la derivación de Diagnóstico). Cada
/// <see cref="OpcionesReconciliador.Intervalo"/> (15 s):
/// <list type="number">
///   <item>Busca, entre todas las compañías, SOLO los ids de las que tienen trabajo: reintentos vencidos, ítems en vuelo cuya espera
///   venció («no salió», pausa del freno) o que no cambian desde hace <see cref="OpcionesReconciliador.Margen"/> (mensaje perdido), o
///   pendientes de crédito (el tope global los pudo dejar esperando). Es un conteo de ids, sin leer filas de otra compañía.</item>
///   <item>Por compañía, con su empresa fijada y en su transacción: <c>REINTENTO_PROGRAMADO</c> vencido ⇒ <c>PENDIENTE</c> (máquina
///   de estados); en vuelo vencido o colgado ⇒ el mismo <see cref="LiquidacionSolicitada"/> otra vez (nuevo <c>eventId</c>); y libera
///   el crédito (<see cref="DespachadorCredito"/>).</item>
/// </list>
/// Un conflicto de concurrencia con el consumidor deja esa compañía para la vuelta siguiente; una vuelta que falla (base caída) se
/// reintenta en la siguiente. Logs: conteos.
/// </summary>
/// <example>
/// <code>
/// var vuelta = await reconciliador.ReconciliarAsync(ct); // { Empresas, Reprogramados, Republicados, Liberados }
/// </code>
/// </example>
public sealed partial class ReconciliadorEjecucion(
    IServiceScopeFactory scopes, IOptions<OpcionesEjecucion> opciones, TimeProvider reloj, ILogger<ReconciliadorEjecucion> log) : BackgroundService
{
    public async Task<VueltaEjecucion> ReconciliarAsync(CancellationToken ct)
    {
        var o = opciones.Value;
        var ahora = reloj.GetUtcNow();
        var limite = ahora - o.Reconciliador.Margen;
        List<Guid> empresas;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
            empresas = await db.Database.SqlQuery<Guid>($"""
                SELECT DISTINCT tenant_id AS "Value" FROM impuestos.liquidacion_items
                WHERE estado = 'PENDIENTE'
                   OR (estado = 'REINTENTO_PROGRAMADO' AND proximo_intento_en <= {ahora})
                   OR (estado IN ('CONSULTANDO_RUNT', 'ENVIANDO')
                       AND ((proximo_intento_en IS NOT NULL AND proximo_intento_en <= {ahora} AND actualizado_en <= proximo_intento_en)
                            OR actualizado_en < {limite}))
                LIMIT {o.Reconciliador.Maximo}
                """).ToListAsync(ct).ConfigureAwait(false);
        }

        int reprogramados = 0, republicados = 0, liberados = 0;
        foreach (var empresa in empresas)
        {
            try
            {
                var (r, p, l) = await ReconciliarEmpresaAsync(empresa, ahora, limite, ct).ConfigureAwait(false);
                reprogramados += r;
                republicados += p;
                liberados += l;
            }
            catch (DbUpdateConcurrencyException)
            {
                Pospuesta(log, empresa);
            }
        }

        var vuelta = new VueltaEjecucion(empresas.Count, reprogramados, republicados, liberados);
        if (reprogramados + republicados + liberados > 0)
            Vuelta(log, vuelta.Empresas, reprogramados, republicados, liberados);
        return vuelta;
    }

    private async Task<(int Reprogramados, int Republicados, int Liberados)> ReconciliarEmpresaAsync(
        Guid empresa, DateTimeOffset ahora, DateTimeOffset limite, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
        var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
        var outbox = scope.ServiceProvider.GetRequiredService<IPlatformOutbox>();
        var despachador = scope.ServiceProvider.GetRequiredService<DespachadorCredito>();
        var o = opciones.Value;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var vencidos = await db.Set<LiquidacionItem>()
                .Where(i => i.Estado == EstadoItem.ReintentoProgramado && i.ProximoIntentoEn <= ahora)
                .OrderBy(i => i.Id).Take(o.Reconciliador.Maximo).ToListAsync(ct).ConfigureAwait(false);
            foreach (var item in vencidos)
            {
                var r = TransicionesItem.Aplicar(new SituacionItem(item.Estado, item.IntentosRunt, item.IntentosReenvio, item.ErrorClase), new EventoItem.VenceReintento());
                ConsumidorLiquidacion.AplicarReintento(item, r, ahora, o);
            }

            var enVuelo = DespachadorCredito.EnVuelo.ToList();
            var colgados = await db.Set<LiquidacionItem>()
                .Where(i => enVuelo.Contains(i.Estado)
                    && ((i.ProximoIntentoEn != null && i.ProximoIntentoEn <= ahora && i.ActualizadoEn <= i.ProximoIntentoEn) || i.ActualizadoEn < limite))
                .OrderBy(i => i.Id).Take(o.Reconciliador.Maximo).ToListAsync(ct).ConfigureAwait(false);
            foreach (var item in colgados)
            {
                item.ActualizadoEn = ahora;
                outbox.Enqueue(LiquidacionSolicitada.Tipo, LiquidacionSolicitada.Version, empresa, new LiquidacionSolicitada(item.LiquidacionId, item.Id));
            }

            var liberados = await despachador.LiberarAsync(empresa, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return (vencidos.Count, colgados.Count, liberados);
        }).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(opciones.Value.Reconciliador.Intervalo, reloj, stoppingToken).ConfigureAwait(false);
                await ReconciliarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Una vuelta que falla (base caída) no tumba el servicio: se reintenta en la siguiente.
            catch (Exception ex) when (ex is DbException or DbUpdateException or InvalidOperationException or TimeoutException)
#pragma warning restore CA1031
            {
                VueltaFallida(log, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconciliador de liquidaciones: {Empresas} compañías; {Reprogramados} reintentos vencidos, {Republicados} re-publicados, {Liberados} con crédito.")]
    private static partial void Vuelta(ILogger logger, int empresas, int reprogramados, int republicados, int liberados);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconciliador de liquidaciones: la empresa {Empresa} cambió mientras se reconciliaba; queda para la siguiente vuelta.")]
    private static partial void Pospuesta(ILogger logger, Guid empresa);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconciliador de liquidaciones: la vuelta falló ({Tipo}); se reintenta en la siguiente.")]
    private static partial void VueltaFallida(ILogger logger, string tipo);
}

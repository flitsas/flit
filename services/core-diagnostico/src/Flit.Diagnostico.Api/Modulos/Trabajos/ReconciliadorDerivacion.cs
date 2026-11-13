using System.Data.Common;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Derivacion.Bus;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>Lo que hizo una vuelta del reconciliador de la derivación.</summary>
/// <param name="Revisados">Ítems colgados que encontró.</param>
/// <param name="Creados">Cerrados CREADO porque Trámites sí tenía el borrador (por referencia externa).</param>
/// <param name="Republicados">Re-publicados por primera vez (<c>republicaciones</c> = 1).</param>
/// <param name="Fallidos">Cerrados FALLIDO tras su re-publicación.</param>
/// <param name="EnPausa">El freno estaba abierto (Trámites cayendo) y no hizo nada.</param>
public sealed record VueltaReconciliadorDerivacion(int Revisados, int Creados, int Republicados, int Fallidos, bool EnPausa);

/// <summary>
/// Reconciliador de la derivación masiva (Punto 4, incremento 3, diseño §5.3 punto 7, ADR-0084; patrón de
/// <see cref="ReconciliadorConsultas"/>): la red de seguridad para mensajes que se pierden de verdad (DLQ, caída entre la outbox y el
/// broker) y que dejarían un crédito ocupado para siempre. Cada <see cref="OpcionesReconciliadorDerivacion.Intervalo"/> (60 s), salvo
/// con el freno abierto:
/// <list type="number">
/// <item>Busca en TODAS las empresas los ítems ENVIADO desde antes de <see cref="OpcionesReconciliadorDerivacion.Margen"/> (15 min) y
/// sin una espera de reintento futura (<see cref="ColaDerivacion.BuscarColgadosAsync"/>, solo id y empresa: la única lectura entre
/// compañías del módulo).</item>
/// <item>Por empresa, con la empresa fijada (<see cref="EmpresaDelTrabajo"/>) y en su transacción: pregunta a Trámites por referencia
/// (<c>ObtenerTramitesPorReferencia</c>); sin respuesta, no decide nada en esta vuelta.</item>
/// <item>Si Trámites tiene el borrador: CREADO (<see cref="CierreItemDerivacion"/>). Si no y no se re-publicó: re-publica el mismo
/// evento (nuevo <c>eventId</c>) con <c>enviado_en</c> = ahora. Si ya se re-publicó: FALLIDO TRAMITES_NO_DISPONIBLE (reintentable a
/// mano). Al final libera el crédito de la compañía.</item>
/// </list>
/// Solo se registra con el consumidor. Una vuelta que falla (base caída) se reintenta en la siguiente. Logs: ids y conteos.
/// </summary>
internal sealed partial class ReconciliadorDerivacion(
    IServiceScopeFactory scopes, FrenoTramites freno, IOptions<OpcionesDerivacion> opciones, TimeProvider reloj,
    ILogger<ReconciliadorDerivacion> log) : BackgroundService
{
    /// <summary>Una vuelta: busca los colgados de todas las empresas y reconcilia cada empresa con la suya fijada.</summary>
    public async Task<VueltaReconciliadorDerivacion> ReconciliarAsync(CancellationToken ct)
    {
        if (freno.EnPausa)
            return new VueltaReconciliadorDerivacion(0, 0, 0, 0, EnPausa: true);
        var o = opciones.Value.Reconciliador;
        var ahora = reloj.GetUtcNow();
        IReadOnlyList<ItemColgado> colgados;
        await using (var scope = scopes.CreateAsyncScope())
            colgados = await ColaDerivacion.BuscarColgadosAsync(scope.ServiceProvider.GetRequiredService<DiagnosticoDb>(), ahora - o.Margen, ahora, o.Maximo, ct).ConfigureAwait(false);

        int creados = 0, republicados = 0, fallidos = 0;
        foreach (var grupo in colgados.GroupBy(c => c.TenantId))
        {
            try
            {
                var (c, r, f) = await ReconciliarEmpresaAsync(grupo.Key, [.. grupo.Select(x => x.Id)], ahora - o.Margen, ct).ConfigureAwait(false);
                creados += c;
                republicados += r;
                fallidos += f;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Otro (el consumidor) tocó un ítem mientras tanto: queda para la siguiente vuelta.
                EmpresaPospuesta(log, grupo.Key);
            }
        }

        var vuelta = new VueltaReconciliadorDerivacion(colgados.Count, creados, republicados, fallidos, EnPausa: false);
        if (colgados.Count > 0)
            Vuelta(log, vuelta.Revisados, vuelta.Creados, vuelta.Republicados, vuelta.Fallidos);
        return vuelta;
    }

    private async Task<(int Creados, int Republicados, int Fallidos)> ReconciliarEmpresaAsync(Guid tenant, IReadOnlyList<Guid> ids, DateTimeOffset limite, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(tenant);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var tramites = scope.ServiceProvider.GetRequiredService<IClienteDerivacionTramites>();
        var outbox = scope.ServiceProvider.GetRequiredService<IPlatformOutbox>();
        var despachador = scope.ServiceProvider.GetRequiredService<DespachadorCredito>();

        var vehiculos = await db.Set<ItemDerivacion>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => i.VehiculoId).ToListAsync(ct).ConfigureAwait(false);
        if (vehiculos.Count == 0)
            return (0, 0, 0);
        if (await tramites.ObtenerPorReferenciaAsync(tenant, vehiculos, ct).ConfigureAwait(false) is not { } existentes)
        {
            SinRespuesta(log, tenant, vehiculos.Count);
            return (0, 0, 0);
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            int creados = 0, republicados = 0, fallidos = 0;
            var ahora = reloj.GetUtcNow();
            var items = await db.Set<ItemDerivacion>().Where(i => ids.Contains(i.Id)).ToListAsync(ct).ConfigureAwait(false);
            foreach (var item in items.Where(i => i.Estado == EstadoItemDerivacion.Enviado && i.EnviadoEn < limite && (i.DisponibleDesde is null || i.DisponibleDesde <= ahora)))
            {
                if (existentes.TryGetValue(item.VehiculoId, out var existente))
                {
                    await CerrarAsync(db, item, new ResultadoBorrador.Creado(existente.TramiteId, existente.Numero, YaExistia: true), ahora, ct).ConfigureAwait(false);
                    creados++;
                }
                else if (item.Republicaciones == 0)
                {
                    item.Republicaciones = 1;
                    item.EnviadoEn = ahora;
                    item.DisponibleDesde = null;
                    item.ActualizadoEn = ahora;
                    outbox.Enqueue(DerivacionSolicitada.Tipo, DerivacionSolicitada.VersionContrato, tenant, new DerivacionSolicitada(item.OperacionId, item.Id, item.VehiculoId));
                    republicados++;
                }
                else
                {
                    await CerrarAsync(db, item, new ResultadoBorrador.NoDisponible(Transitorio: false), ahora, ct).ConfigureAwait(false);
                    fallidos++;
                }
            }

            await despachador.LiberarAsync(tenant, ct).ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return (creados, republicados, fallidos);
        }).ConfigureAwait(false);
    }

    private static async Task CerrarAsync(DiagnosticoDb db, ItemDerivacion item, ResultadoBorrador resultado, DateTimeOffset ahora, CancellationToken ct)
    {
        var operacion = await db.Set<OperacionDerivacion>().SingleAsync(o => o.Id == item.OperacionId, ct).ConfigureAwait(false);
        var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == item.VehiculoId, ct).ConfigureAwait(false);
        await CierreItemDerivacion.CerrarAsync(db, operacion, item, vehiculo, resultado, ahora, ct).ConfigureAwait(false);
        await Notificaciones.EscritorNotificaciones.SiTerminoDerivacionMasivaAsync(db, operacion, ahora, ct).ConfigureAwait(false);
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
#pragma warning disable CA1031 // Una vuelta que falla (base o Trámites caídos) no tumba el servicio: se reintenta en la siguiente.
            catch (Exception ex) when (ex is DbException or DbUpdateException or InvalidOperationException or TimeoutException)
#pragma warning restore CA1031
            {
                VueltaFallida(log, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(EventId = 84340, Level = LogLevel.Information, Message = "Reconciliador de derivación: {Revisados} colgados; {Creados} creados en Trámites, {Republicados} re-publicados, {Fallidos} fallidos.")]
    private static partial void Vuelta(ILogger logger, int revisados, int creados, int republicados, int fallidos);

    [LoggerMessage(EventId = 84341, Level = LogLevel.Warning, Message = "Reconciliador de derivación: Trámites no respondió por referencia para la empresa {Empresa} ({Cantidad} ítems); queda para la siguiente vuelta.")]
    private static partial void SinRespuesta(ILogger logger, Guid empresa, int cantidad);

    [LoggerMessage(EventId = 84342, Level = LogLevel.Information, Message = "Reconciliador de derivación: la empresa {Empresa} cambió mientras se reconciliaba; queda para la siguiente vuelta.")]
    private static partial void EmpresaPospuesta(ILogger logger, Guid empresa);

    [LoggerMessage(EventId = 84343, Level = LogLevel.Warning, Message = "Reconciliador de derivación: la vuelta falló ({Tipo}); se reintenta en la siguiente.")]
    private static partial void VueltaFallida(ILogger logger, string tipo);
}

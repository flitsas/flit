using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Conciliacion;

/// <summary>
/// Rondas de conciliación (plan §2.2, F2b-CF4/CF5, DA-6=c) cada <see cref="OpcionesConciliacion.IntervaloTrabajador"/>:
/// <list type="number">
///   <item>Ítems <c>ENVIADO</c> con <c>proximo_poll_en</c> vencido (o sin él) de lotes no cerrados a mano: <c>GET
///   /api/v1/consultas/{id}</c>; un resultado final se aplica con <see cref="AplicadorResultado"/> y lo demás se reprograma con
///   <see cref="CalendarioConciliacion"/> (sin vencimiento).</item>
///   <item>Ítems <c>INCIERTO</c> listos para correlacionar: los sin <c>callbackUrl</c> de inmediato y los con token tras
///   <see cref="OpcionesConciliacion.EsperaIncierto"/> (<see cref="CorrelacionIncierto"/>).</item>
/// </list>
/// Entre empresas solo se leen ids de compañía por SQL parametrizado; cada compañía se lee y cada ítem se procesa en su scope con
/// la empresa fijada. Un fallo de un ítem no corta la ronda. Se apaga con <c>Impuestos:Conciliacion:TrabajadorActivo=false</c>.
/// </summary>
/// <example>
/// <code>
/// var procesados = await trabajador.EjecutarRondaAsync(ct);
/// </code>
/// </example>
public sealed partial class TrabajadorConciliacion(
    IServiceScopeFactory scopes, OpcionesConciliacion opciones, TimeProvider reloj, ILogger<TrabajadorConciliacion> log) : BackgroundService
{
    public async Task<int> EjecutarRondaAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var limiteIncierto = ahora - opciones.EsperaIncierto;
        List<Guid> conEnviados;
        List<Guid> conInciertos;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
            conEnviados = await db.Database.SqlQuery<Guid>(
                $"SELECT DISTINCT i.tenant_id AS \"Value\" FROM impuestos.liquidacion_items i JOIN impuestos.liquidaciones l ON l.id = i.liquidacion_id WHERE i.estado = 'ENVIADO' AND (i.proximo_poll_en IS NULL OR i.proximo_poll_en <= {ahora}) AND l.estado <> 'CERRADO_MANUALMENTE' LIMIT {opciones.LotePorRonda}")
                .ToListAsync(ct).ConfigureAwait(false);
            conInciertos = await db.Database.SqlQuery<Guid>(
                $"SELECT DISTINCT tenant_id AS \"Value\" FROM impuestos.liquidacion_items WHERE estado = 'INCIERTO' AND (proximo_poll_en IS NULL OR proximo_poll_en <= {ahora}) AND (con_callback = false OR token_hash IS NULL OR COALESCE(enviado_en, actualizado_en) <= {limiteIncierto}) LIMIT {opciones.LotePorRonda}")
                .ToListAsync(ct).ConfigureAwait(false);
        }

        var procesados = 0;
        foreach (var empresa in conEnviados)
        {
            foreach (var item in await ItemsAsync(empresa, ahora, inciertos: false, limiteIncierto, ct).ConfigureAwait(false))
                procesados += await ProtegidoAsync(empresa, item, sp => ConsultarEnviadoAsync(sp, empresa, item, ahora, ct), ct).ConfigureAwait(false);
        }

        foreach (var empresa in conInciertos)
        {
            foreach (var item in await ItemsAsync(empresa, ahora, inciertos: true, limiteIncierto, ct).ConfigureAwait(false))
                procesados += await ProtegidoAsync(empresa, item, sp => sp.GetRequiredService<CorrelacionIncierto>().ResolverAsync(empresa, item, ct), ct).ConfigureAwait(false);
        }

        return procesados;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarRondaAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                FalloRonda(log, ex.GetType().Name);
            }

            try
            {
                await Task.Delay(opciones.IntervaloTrabajador, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<List<Guid>> ItemsAsync(Guid empresa, DateTimeOffset ahora, bool inciertos, DateTimeOffset limiteIncierto, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
        var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
        var vencidos = db.Set<LiquidacionItem>().Where(i => i.ProximoPollEn == null || i.ProximoPollEn <= ahora);
        var consulta = inciertos
            ? vencidos.Where(i => i.Estado == EstadoItem.Incierto
                && (!i.ConCallback || i.TokenHash == null || (i.EnviadoEn ?? i.ActualizadoEn) <= limiteIncierto))
            : vencidos.Where(i => i.Estado == EstadoItem.Enviado
                && db.Set<Liquidacion>().Any(l => l.Id == i.LiquidacionId && l.Estado != EstadoLote.CerradoManualmente));
        return await consulta.OrderBy(i => i.ProximoPollEn).Select(i => i.Id).Take(opciones.LotePorRonda).ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<int> ProtegidoAsync<T>(Guid empresa, Guid item, Func<IServiceProvider, Task<T>> paso, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
            await paso(scope.ServiceProvider).ConfigureAwait(false);
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            FalloItem(log, item, ex.GetType().Name);
            return 0;
        }
    }

    private async Task<bool> ConsultarEnviadoAsync(IServiceProvider sp, Guid empresa, Guid itemId, DateTimeOffset ahora, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ImpuestosDb>();
        var item = await db.Set<LiquidacionItem>().AsNoTracking().Where(i => i.Id == itemId && i.Estado == EstadoItem.Enviado)
            .Select(i => new { i.ConsultaApiId, i.EnviadoEn, i.ActualizadoEn, i.ConCallback })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
            return false;

        if (item.ConsultaApiId is { } consultaId)
        {
            var lectura = await sp.GetRequiredService<LectorResultado>().ConsultarAsync(empresa, consultaId, ct).ConfigureAwait(false);
            if (lectura is { Clase: ClaseLectura.Leida, Resultado.EsFinal: true }
                && await sp.GetRequiredService<AplicadorResultado>().AplicarAsync(empresa, itemId, lectura.Resultado, ct).ConfigureAwait(false) == Aplicacion.Aplicado)
                return true;
            if (lectura.Clase is ClaseLectura.CredencialRechazada or ClaseLectura.SinCredencial)
            {
                MetricasImpuestos.ContarAlerta("conciliacion_sin_credencial");
                SinCredencial(log, empresa, lectura.Clase);
                // F1a-CF12: un 401 en uso lleva la compañía a REQUIERE_CREDENCIALES, igual que al liquidar (M09).
                if (lectura.Clase == ClaseLectura.CredencialRechazada)
                    await sp.GetRequiredService<CredencialRechazadaEnUso>().AplicarAsync(empresa, ct).ConfigureAwait(false);
            }
        }

        var modoRespaldo = await db.Set<AprovisionamientoEmpresa>().Where(a => a.TenantId == empresa).Select(a => a.ModoRespaldoFirma)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var siguiente = CalendarioConciliacion.Siguiente(
            item.EnviadoEn ?? item.ActualizadoEn, ahora, CalendarioConciliacion.SinWebhook(item.ConCallback, modoRespaldo), opciones);
        await db.Set<LiquidacionItem>().Where(i => i.Id == itemId && i.Estado == EstadoItem.Enviado)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ProximoPollEn, siguiente), ct).ConfigureAwait(false);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "La conciliación del ítem {Item} falló ({Error}); se reintenta en la siguiente ronda.")]
    private static partial void FalloItem(ILogger logger, Guid item, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La conciliación de la empresa {Empresa} no tiene una api_key válida ({Clase}); el ítem sigue reprogramado.")]
    private static partial void SinCredencial(ILogger logger, Guid empresa, ClaseLectura clase);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La ronda de conciliación falló ({Error}); se reintenta en el siguiente intervalo.")]
    private static partial void FalloRonda(ILogger logger, string error);
}

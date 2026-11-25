using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Liquidaciones;

/// <summary>Cuerpo opcional de <c>POST /liquidaciones/{id}/cerrar</c>.</summary>
public sealed record CierreEntrada(string? Motivo);

/// <summary>
/// Cierre manual de un lote en curso (plan §2.3 y §3.3; DA-6b, F3-CF12). Ruta <c>POST /liquidaciones/{id}/cerrar</c> con el permiso
/// <c>impuestos.liquidaciones.cerrar</c> (Administrador Impuestos) o el Super Admin con <c>?empresa</c>; el Gestor recibe 403.
/// <list type="number">
///   <item>El motivo admite hasta <see cref="LargoMaximoMotivo"/> caracteres (si no, 422 en el campo <c>motivo</c>).</item>
///   <item>En una transacción con el candado del crédito y luego la cabecera bloqueada (orden de <see cref="DespachadorCredito"/>; la
///   cabecera, como el aplicador de resultados, con su compañía): un lote de otra compañía no existe (404); uno terminado responde 409
///   <c>YA_TERMINADA</c>. Si un ítem cambia mientras tanto, se relee y se vuelve a cerrar (hasta 3 veces).</item>
///   <item>Cada ítem no final pasa a <c>SIN_RESULTADO_LOTE_CERRADO</c> (máquina del dominio) sin perder su consulta ni su token, para
///   aceptar un resultado tardío; el lote, a <c>CERRADO_MANUALMENTE</c> con <c>cerrada_por</c>, <c>cerrada_en</c> y
///   <c>terminada_en</c>. Motivo y nombre de quien cierra se escriben en <see cref="RegistrarCierre"/> (columnas de la 1.2.0).</item>
///   <item><see cref="EvaluadorTerminacion"/> recalcula los contadores y emite el aviso una vez; luego se libera el crédito que tenían
///   los ítems en vuelo.</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// CerrarLiquidacion.Map(LiquidacionesEndpoints.Map(api));
/// </code>
/// </example>
public sealed partial class CerrarLiquidacion(IServiceScopeFactory scopes, ConsultaLiquidaciones consulta, TimeProvider reloj, ILogger<CerrarLiquidacion> log)
{
    public const int LargoMaximoMotivo = 300;

    private const int IntentosPorConcurrencia = 3;

    public static RouteGroupBuilder Map(RouteGroupBuilder liquidaciones)
    {
        ArgumentNullException.ThrowIfNull(liquidaciones);
        liquidaciones.MapPost("/{id:guid}/cerrar", CerrarEndpointAsync).RequirePermiso(Permisos.LiquidacionesCerrar).CuerpoAcotado().WithName("CerrarLiquidacion");
        return liquidaciones;
    }

    public async Task<LiquidacionDto> CerrarAsync(
        Guid empresa, Guid id, string? motivo, Guid actor, string? actorNombre, ReferenciaDto empresaRef, CancellationToken ct)
    {
        if (motivo is { Length: > LargoMaximoMotivo })
            throw ErroresLiquidaciones.MotivoInvalido();
        var motivoLimpio = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();

        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
            var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
            var evaluador = scope.ServiceProvider.GetRequiredService<EvaluadorTerminacion>();
            var despachador = scope.ServiceProvider.GetRequiredService<DespachadorCredito>();
            for (var intento = 1; ; intento++)
            {
                try
                {
                    await CerrarEnTransaccionAsync(db, evaluador, despachador, empresa, id, motivoLimpio, actor, actorNombre, empresaRef, ct).ConfigureAwait(false);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (intento < IntentosPorConcurrencia)
                {
                    // Un ítem del lote cambió mientras se cerraba (p. ej. el reconciliador): se relee todo y se vuelve a cerrar.
                    Reintento(log, id, intento);
                }
            }
        }

        return await consulta.LiquidacionAsync(empresa, id, empresaRef, ct).ConfigureAwait(false);
    }

    private async Task CerrarEnTransaccionAsync(
        ImpuestosDb db, EvaluadorTerminacion evaluador, DespachadorCredito despachador, Guid empresa, Guid id, string? motivoLimpio, Guid actor,
        string? actorNombre, ReferenciaDto empresaRef, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var lote = await db.Set<Liquidacion>().SingleOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false)
                ?? throw ErroresLiquidaciones.NoEncontrada();
            // Orden de bloqueos (DespachadorCredito): el candado del crédito ANTES que la cabecera, porque al final se libera crédito.
            await DespachadorCredito.TomarCandadoAsync(db, ct).ConfigureAwait(false);
            // Un cierre o un aplicador por lote a la vez: la cabecera queda bloqueada hasta el commit.
            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM impuestos.liquidaciones WHERE id = {id} AND tenant_id = {empresa} FOR UPDATE", ct).ConfigureAwait(false);
            await db.Entry(lote).ReloadAsync(ct).ConfigureAwait(false);
            if (CierreLote.EsTerminal(lote.Estado))
                throw ErroresLiquidaciones.YaTerminada();

            var ahora = reloj.GetUtcNow();
            var items = await db.Set<LiquidacionItem>().Where(i => i.LiquidacionId == id).ToListAsync(ct).ConfigureAwait(false);
            foreach (var item in items.Where(i => !EstadosItem.EsFinal(i.Estado)))
            {
                item.Estado = TransicionesItem.Aplicar(
                    new SituacionItem(item.Estado, item.IntentosRunt, item.IntentosReenvio, item.ErrorClase), new EventoItem.CierreManual()).Nueva.Estado;
                item.ProximoIntentoEn = null;
                item.ProximoPollEn = null;
                item.ActualizadoEn = ahora;
            }

            var anterior = lote.Estado;
            lote.Estado = CierreLote.CerrarManualmente(anterior);
            lote.CerradaPor = actor;
            lote.CerradaEn = ahora;
            lote.TerminadaEn ??= ahora;
            RegistrarCierre(lote, motivoLimpio, actorNombre);
            await evaluador.EvaluarAsync(lote, [.. items.Select(i => new EstadoParaContar(i.Estado, i.ResultadoStatus))], ahora, ct, anterior, empresaRef.Nombre)
                .ConfigureAwait(false);
            await despachador.LiberarAsync(empresa, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            var sinResultado = items.Count(i => i.Estado == EstadoItem.SinResultadoLoteCerrado);
            Cerrada(log, empresa, id, sinResultado);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Motivo del cierre y nombre de quien cierra según <see cref="INombreActor"/> (columnas <c>motivo_cierre</c> y
    /// <c>cerrada_por_nombre</c> de la 1.2.0). Es el único punto que los escribe; un nombre vacío queda null.
    /// </summary>
    internal static void RegistrarCierre(Liquidacion lote, string? motivo, string? nombre)
    {
        ArgumentNullException.ThrowIfNull(lote);
        lote.MotivoCierre = motivo;
        lote.CerradaPorNombre = NombreDelActor(nombre);
    }

    /// <summary>Máximo de <c>creada_por_nombre</c> y <c>cerrada_por_nombre</c>.</summary>
    public const int LargoMaximoNombreActor = 200;

    /// <summary>El nombre de quien actúa, recortado al largo de la columna; null si no hay.</summary>
    internal static string? NombreDelActor(string? nombre)
    {
        var limpio = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim();
        return limpio is { Length: > LargoMaximoNombreActor } ? limpio[..LargoMaximoNombreActor] : limpio;
    }

    private static async Task<IResult> CerrarEndpointAsync(
        Guid id, HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, CerrarLiquidacion cerrar,
        INombreActor nombres, CancellationToken ct)
    {
        var (empresa, referencia) = await LiquidacionesEndpoints.EmpresaDeEscrituraAsync(
            http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, ct).ConfigureAwait(false);
        var entrada = await LeerEntradaAsync(http, ct).ConfigureAwait(false);
        var actorNombre = await nombres.NombreAsync(usuario, ct).ConfigureAwait(false);
        return Results.Ok(await cerrar.CerrarAsync(empresa, id, entrada?.Motivo, usuario.Id, actorNombre, referencia, ct).ConfigureAwait(false));
    }

    /// <summary>El cuerpo es opcional: vacío o sin JSON es «sin motivo»; un JSON mal formado, también (no hay código para él).</summary>
    private static async Task<CierreEntrada?> LeerEntradaAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength is 0 || !http.Request.HasJsonContentType())
            return null;
        try
        {
            return await http.Request.ReadFromJsonAsync<CierreEntrada>(ct).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cierre de la liquidación {Liquidacion}: cambió mientras se cerraba (intento {Intento}); se reintenta.")]
    private static partial void Reintento(ILogger logger, Guid liquidacion, int intento);

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación {Liquidacion} de la empresa {Empresa} cerrada a mano: {SinResultado} placas sin resultado.")]
    private static partial void Cerrada(ILogger logger, Guid empresa, Guid liquidacion, int sinResultado);
}

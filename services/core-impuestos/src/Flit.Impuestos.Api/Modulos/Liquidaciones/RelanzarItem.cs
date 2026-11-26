using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Liquidaciones;

/// <summary>
/// Ruta <c>POST /liquidaciones/{id}/items/{itemId}/relanzar</c> con <c>impuestos.liquidaciones.crear</c> (impuestos.v1.yaml 1.3.0):
/// regla de <c>?empresa</c> (<see cref="LiquidacionesEndpoints.EmpresaDeEscrituraAsync"/>), el nuevo intento lo abre
/// <see cref="RelanzamientoItem"/> y responde 200 <c>LiquidacionConItems</c> de la MISMA liquidación. El cuerpo
/// (<c>confirmarPosibleDuplicado</c> de la 1.2.x) se acepta y se ignora: no hay 422.
/// </summary>
/// <example>
/// <code>
/// RelanzarItem.Map(LiquidacionesEndpoints.Map(api));
/// </code>
/// </example>
public static class RelanzarItem
{
    public static RouteGroupBuilder Map(RouteGroupBuilder liquidaciones)
    {
        ArgumentNullException.ThrowIfNull(liquidaciones);
        liquidaciones.MapPost("/{id:guid}/items/{itemId:guid}/relanzar", RelanzarAsync)
            .RequirePermiso(Permisos.LiquidacionesCrear).CuerpoAcotado().WithName("RelanzarItemLiquidacion");
        return liquidaciones;
    }

    private static async Task<IResult> RelanzarAsync(
        Guid id, Guid itemId, HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito,
        RelanzamientoItem relanzamiento, INombreActor nombres, CancellationToken ct)
    {
        var (empresa, referencia) = await LiquidacionesEndpoints.EmpresaDeEscrituraAsync(
            http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, ct).ConfigureAwait(false);
        var actorNombre = await nombres.NombreAsync(usuario, ct).ConfigureAwait(false);
        return Results.Ok(await relanzamiento.RelanzarAsync(empresa, id, itemId, actorNombre, referencia, ct).ConfigureAwait(false));
    }
}

/// <summary>
/// Relanzar = nuevo intento del MISMO ítem (R3, decisión del usuario 2026-11-26; plan §3.2). No crea otra liquidación:
/// <list type="number">
///   <item>Lote de otra compañía ⇒ 404 <c>LIQUIDACION_NO_ENCONTRADA</c>; ítem que no es del lote ⇒ 404 <c>ITEM_NO_ENCONTRADO</c>.</item>
///   <item>Aprovisionamiento como la individual (<see cref="CrearLiquidacionIndividual.ExigirAprovisionamientoAsync"/>, 409).</item>
///   <item>En una transacción con el orden de bloqueos del <see cref="DespachadorCredito"/> (candado del crédito → cabecera del lote
///   <c>FOR UPDATE</c> → ítem <c>FOR UPDATE</c>): el ítem debe admitir <see cref="EventoItem.Relanzar"/> (<c>INCIERTO_REQUIERE_DECISION</c>,
///   <c>DATOS_INCOMPLETOS</c>, <c>ERROR</c> reintentable o de placa no inscrita) y el lote no estar <c>CERRADO_MANUALMENTE</c>; si no,
///   409 <c>NO_RELANZABLE</c>. Dos relanzamientos seguidos o simultáneos quedan en serie: el segundo ve el ítem en vuelo (409).</item>
///   <item>Guarda la foto del intento en curso en <c>liquidacion_item_intentos</c> (<see cref="LiquidacionItemIntento.De"/>), sube
///   <c>numero_intento</c>, limpia error, resultado (también el cifrado), total, warnings, token, consulta, reintentos y las vigencias
///   (<c>liquidacion_periodos</c>), y deja el ítem en <c>PENDIENTE</c>, como nace al crear. <c>posible_duplicado</c> no se marca.</item>
///   <item>El lote vuelve a estar en curso: un lote terminado pasa a <c>EN_PROCESO</c> (sin <c>terminada_en</c>) y sus contadores se
///   recalculan con <see cref="ContadoresLote.Recalcular"/>. <see cref="DespachadorCredito.LiberarAsync"/> le da crédito si cabe y deja
///   <c>impuestos.liquidacion.solicitada</c> (eventId nuevo) en la outbox, igual que al crear.</item>
/// </list>
/// Logs: ids y números de intento; nunca la placa ni el nombre de quien relanza.
/// </summary>
/// <example>
/// <code>
/// var lote = await relanzamiento.RelanzarAsync(empresa, loteId, itemId, "operador@empresa.test", empresaRef, ct); // misma liquidación
/// </code>
/// </example>
public sealed partial class RelanzamientoItem(
    IServiceScopeFactory scopes, ConsultaLiquidaciones consulta, TimeProvider reloj, ILogger<RelanzamientoItem> log)
{
    public async Task<LiquidacionConItemsDto> RelanzarAsync(
        Guid empresa, Guid id, Guid itemId, string? actorNombre, ReferenciaDto empresaRef, CancellationToken ct)
    {
        int numero;
        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
            var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
            var despachador = scope.ServiceProvider.GetRequiredService<DespachadorCredito>();
            if (!await db.Set<Liquidacion>().AnyAsync(l => l.Id == id, ct).ConfigureAwait(false))
                throw ErroresLiquidaciones.NoEncontrada();
            if (!await db.Set<LiquidacionItem>().AnyAsync(i => i.Id == itemId && i.LiquidacionId == id, ct).ConfigureAwait(false))
                throw ErroresLiquidaciones.ItemNoEncontrado();
            await CrearLiquidacionIndividual.ExigirAprovisionamientoAsync(db, empresa, ct).ConfigureAwait(false);

            numero = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                // Orden de bloqueos del DespachadorCredito: candado del crédito (esta transacción libera) → cabecera → ítem.
                await DespachadorCredito.TomarCandadoAsync(db, ct).ConfigureAwait(false);
                await db.Database.ExecuteSqlAsync(
                    $"SELECT 1 FROM impuestos.liquidaciones WHERE id = {id} AND tenant_id = {empresa} FOR UPDATE", ct).ConfigureAwait(false);
                await db.Database.ExecuteSqlAsync(
                    $"SELECT 1 FROM impuestos.liquidacion_items WHERE id = {itemId} AND tenant_id = {empresa} FOR UPDATE", ct).ConfigureAwait(false);
                var lote = await db.Set<Liquidacion>().SingleAsync(l => l.Id == id, ct).ConfigureAwait(false);
                var item = await db.Set<LiquidacionItem>().SingleAsync(i => i.Id == itemId, ct).ConfigureAwait(false);
                if (lote.Estado == EstadoLote.CerradoManualmente)
                    throw ErroresLiquidaciones.NoRelanzable();
                var nueva = NuevoIntento(item);

                var ahora = reloj.GetUtcNow();
                db.Add(LiquidacionItemIntento.De(item, ahora, CerrarLiquidacion.NombreDelActor(actorNombre)));
                Reiniciar(item, nueva, ahora);
                await db.Set<LiquidacionPeriodo>().Where(p => p.ItemId == itemId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

                if (CierreLote.EsTerminal(lote.Estado))
                {
                    lote.Estado = EstadoLote.EnProceso;
                    lote.TerminadaEn = null;
                }

                var estados = await db.Set<LiquidacionItem>().AsNoTracking().Where(i => i.LiquidacionId == id && i.Id != itemId)
                    .Select(i => new EstadoParaContar(i.Estado, i.ResultadoStatus)).ToListAsync(ct).ConfigureAwait(false);
                estados.Add(new EstadoParaContar(item.Estado, item.ResultadoStatus));
                ContadoresLote.Recalcular(lote, estados, ahora);

                await despachador.LiberarAsync(empresa, ct).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return item.NumeroIntento;
            }).ConfigureAwait(false);
        }

        Relanzado(log, empresa, id, itemId, numero);
        return await consulta.ConItemsAsync(empresa, id, empresaRef, ct).ConfigureAwait(false);
    }

    /// <summary>La situación del nuevo intento según la tabla del dominio; 409 <c>NO_RELANZABLE</c> si el estado no lo admite.</summary>
    private static SituacionItem NuevoIntento(LiquidacionItem item)
    {
        try
        {
            return TransicionesItem.Aplicar(
                new SituacionItem(item.Estado, item.IntentosRunt, item.IntentosReenvio, item.ErrorClase, item.ErrorCodigo, item.ErrorMensaje),
                new EventoItem.Relanzar()).Nueva;
        }
        catch (TransicionInvalida)
        {
            throw ErroresLiquidaciones.NoRelanzable();
        }
    }

    /// <summary>El ítem vuelve a nacer en <paramref name="nueva"/> (<c>PENDIENTE</c>) con el siguiente número de intento y sin rastro del anterior.</summary>
    private static void Reiniciar(LiquidacionItem item, SituacionItem nueva, DateTimeOffset ahora)
    {
        item.NumeroIntento++;
        item.Estado = nueva.Estado;
        item.IntentosRunt = nueva.IntentosRunt;
        item.IntentosReenvio = nueva.IntentosReenvio;
        item.ErrorClase = nueva.ClaseError;
        item.ErrorCodigo = null;
        item.ErrorMensaje = null;
        item.CampoFaltante = null;
        item.ConsultaApiId = null;
        item.TokenHash = null;
        item.ConCallback = false;
        item.ProximoIntentoEn = null;
        item.EnviadoEn = null;
        item.ProximoPollEn = null;
        item.AttemptApiMax = 0;
        item.ConsultaStatus = null;
        item.ResultadoStatus = null;
        item.Parcial = false;
        item.TotalAPagar = null;
        item.ResultadoCifrado = null;
        item.Warnings = [];
        item.ResultadoTardio = false;
        item.ActualizadoEn = ahora;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación {Liquidacion} de la empresa {Empresa}: ítem {ItemId} relanzado, intento {Numero}.")]
    private static partial void Relanzado(ILogger logger, Guid empresa, Guid liquidacion, Guid itemId, int numero);
}

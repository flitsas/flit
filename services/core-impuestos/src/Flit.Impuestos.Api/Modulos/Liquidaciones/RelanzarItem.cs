using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Liquidaciones;

/// <summary>Cuerpo de <c>POST /liquidaciones/{id}/items/{itemId}/relanzar</c>.</summary>
public sealed record RelanzarEntrada(bool? ConfirmarPosibleDuplicado);

/// <summary>
/// Relanzamiento de una placa (plan §3.2; impuestos.v1.yaml 1.1.0, deltas 2 y 5). Ruta
/// <c>POST /liquidaciones/{id}/items/{itemId}/relanzar</c> con <c>impuestos.liquidaciones.crear</c>:
/// <list type="number">
///   <item>Regla de <c>?empresa</c> (<see cref="LiquidacionesEndpoints.EmpresaDeEscrituraAsync"/>).</item>
///   <item>Lote de otra compañía ⇒ 404 <c>LIQUIDACION_NO_ENCONTRADA</c>; ítem que no es del lote ⇒ 404 <c>ITEM_NO_ENCONTRADO</c>.</item>
///   <item>Sin <c>confirmarPosibleDuplicado: true</c> ⇒ 422 <c>CONFIRMACION_REQUERIDA</c>.</item>
///   <item>Solo desde <c>INCIERTO_REQUIERE_DECISION</c> o <c>ERROR</c> <c>REINTENTABLE</c> (<see cref="EventoItem.Relanzar"/> del
///   dominio) ⇒ si no, 409 <c>NO_RELANZABLE</c>.</item>
///   <item>Crea una individual nueva con <see cref="CrearLiquidacionIndividual"/> (mismo aprovisionamiento exigido, 409) con la placa
///   del ítem, <c>relanzado_desde</c> y <c>posible_duplicado=true</c>. El ítem y el lote originales no cambian.</item>
/// </list>
/// Responde 201 <c>LiquidacionConItems</c>.
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

    /// <summary>La placa del ítem a relanzar, si el lote es de la compañía, el ítem es suyo, está confirmado y su estado lo admite.</summary>
    internal static async Task<string> PlacaRelanzableAsync(ImpuestosDb db, Guid id, Guid itemId, bool confirmado, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!await db.Set<Liquidacion>().AnyAsync(l => l.Id == id, ct).ConfigureAwait(false))
            throw ErroresLiquidaciones.NoEncontrada();
        var item = await db.Set<LiquidacionItem>().AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.LiquidacionId == id, ct).ConfigureAwait(false)
            ?? throw ErroresLiquidaciones.ItemNoEncontrado();
        if (!confirmado)
            throw ErroresLiquidaciones.ConfirmacionRequerida();
        try
        {
            TransicionesItem.Aplicar(new SituacionItem(item.Estado, item.IntentosRunt, item.IntentosReenvio, item.ErrorClase), new EventoItem.Relanzar());
        }
        catch (TransicionInvalida)
        {
            throw ErroresLiquidaciones.NoRelanzable();
        }

        return item.Placa;
    }

    private static async Task<IResult> RelanzarAsync(
        Guid id, Guid itemId, HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito,
        AlmacenCredenciales almacen, CrearLiquidacionIndividual crear, INombreActor nombres, CancellationToken ct)
    {
        var (empresa, referencia) = await LiquidacionesEndpoints.EmpresaDeEscrituraAsync(
            http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, ct).ConfigureAwait(false);
        var entrada = await LeerEntradaAsync(http, ct).ConfigureAwait(false);
        string placa;
        await using (var db = almacen.ContextoDe(empresa))
            placa = await PlacaRelanzableAsync(db, id, itemId, entrada?.ConfirmarPosibleDuplicado == true, ct).ConfigureAwait(false);
        var actorNombre = await nombres.NombreAsync(usuario, ct).ConfigureAwait(false);
        var creada = await crear.CrearAsync(empresa, placa, usuario.Id, referencia, ct, relanzadoDesde: itemId, actorNombre: actorNombre).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}{LiquidacionesEndpoints.Ruta}/{creada.Id}", creada);
    }

    /// <summary>Cuerpo ausente, vacío o ilegible equivale a «sin confirmar» (422 <c>CONFIRMACION_REQUERIDA</c>).</summary>
    private static async Task<RelanzarEntrada?> LeerEntradaAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength is 0 || !http.Request.HasJsonContentType())
            return null;
        try
        {
            return await http.Request.ReadFromJsonAsync<RelanzarEntrada>(ct).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

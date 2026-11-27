using Flit.Impuestos.Api.Infraestructura.Archivos;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Modulos.Liquidaciones;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Recibos;

/// <summary>
/// Recibos PDF de M11 (plan §2.4 y §5.1; impuestos.v1.yaml 1.2.0 <c>descargarRecibo</c>):
/// <c>GET /liquidaciones/{id}/items/{itemId}/recibos/{reciboId}</c> con <c>impuestos.recibos.descargar</c>. Sirve el PDF desde el
/// almacén de la suite (nunca por proxy al proveedor: el navegador no envía ni recibe la api_key) con las cabeceras de
/// <see cref="DescargaSeguraFiltro"/>. Un recibo que no está <c>GUARDADO</c> (o cuyo archivo ya no está en el almacén) responde 409
/// <c>RECIBO_NO_DISPONIBLE</c> con su <c>motivo</c>; la liquidación, el ítem o el recibo que no existen en la compañía, 404 (otra
/// compañía responde igual que inexistente). <c>?empresa</c> sigue la regla de <see cref="EmpresaExplicita"/> y la lectura del Super
/// Admin queda auditada (<see cref="LiquidacionesEndpoints.EmpresaDeLecturaAsync"/>).
/// </summary>
/// <example>
/// <code>
/// var liquidaciones = LiquidacionesEndpoints.Map(api);
/// RecibosEndpoints.Map(liquidaciones);
/// </code>
/// </example>
internal static class RecibosEndpoints
{
    public const string RutaRecibo = "/{id:guid}/items/{itemId:guid}/recibos/{reciboId:guid}";
    public const string CodigoNoEncontrado = "RECIBO_NO_ENCONTRADO";
    public const string CodigoNoDisponible = "RECIBO_NO_DISPONIBLE";
    public const string LiteralNoEncontrado = "El recibo no existe.";
    public const string LiteralNoDisponible = "El recibo no está disponible.";

    /// <summary>Almacén de archivos (fail-closed), descarga, consumidor de <see cref="RecibosDescargar.Cola"/> y sus reintentos.</summary>
    public static IServiceCollection AddRecibosImpuestos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddAlmacenArchivos(configuration);
        services.AddScoped<DescargaRecibos>();
        services.AddFlitConsumer<ImpuestosDb, ConsumidorRecibos, RecibosDescargar>(
            configuration, RecibosDescargar.Cola, RecibosDescargar.Productor, [RecibosDescargar.Tipo], ConsumidorRecibos.ConfigurarCola);
        return services;
    }

    public static RouteGroupBuilder Map(RouteGroupBuilder liquidaciones)
    {
        ArgumentNullException.ThrowIfNull(liquidaciones);
        liquidaciones.MapGet(RutaRecibo, DescargarAsync).RequirePermiso(Permisos.RecibosDescargar).DescargaSegura().WithName("DescargarRecibo");
        return liquidaciones;
    }

    private static async Task<IResult> DescargarAsync(
        Guid id, Guid itemId, Guid reciboId, HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito,
        AuditoriaLecturaBackoffice auditoria, AlmacenCredenciales almacen, IAlmacenArchivos archivos, CancellationToken ct)
    {
        var (empresa, _) = await LiquidacionesEndpoints.EmpresaDeLecturaAsync(
            http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, auditoria, ct).ConfigureAwait(false);
        await using var db = almacen.ContextoDe(empresa);
        if (!await db.Set<Liquidacion>().AnyAsync(l => l.Id == id, ct).ConfigureAwait(false))
            throw ErroresLiquidaciones.NoEncontrada();
        var placa = await db.Set<LiquidacionItem>().AsNoTracking().Where(i => i.Id == itemId && i.LiquidacionId == id)
            .Select(i => i.Placa).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw ErroresLiquidaciones.ItemNoEncontrado();
        var recibo = await db.Set<Recibo>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == reciboId && r.ItemId == itemId, ct).ConfigureAwait(false)
            ?? throw new ReglaException(CodigoNoEncontrado, LiteralNoEncontrado, StatusCodes.Status404NotFound);
        if (recibo is not { Estado: EstadoRecibo.Guardado, ReferenciaAlmacen: { Length: > 0 } referencia })
            return NoDisponible(recibo.Motivo);

        var pdf = await archivos.LeerAsync(referencia, ct).ConfigureAwait(false);
        return pdf is null ? NoDisponible(recibo.Motivo) : Results.File(pdf, DescargaRecibos.ContentTypePdf, NombreArchivo(placa, recibo));
    }

    /// <summary>409 <c>RECIBO_NO_DISPONIBLE</c> con el literal del contrato y el <c>motivo</c> del recibo (null si no tiene).</summary>
    private static IResult NoDisponible(string? motivo) =>
        Results.Json(
            new Dictionary<string, object?> { ["code"] = CodigoNoDisponible, ["error"] = LiteralNoDisponible, ["motivo"] = motivo },
            statusCode: StatusCodes.Status409Conflict);

    /// <summary><c>recibo-{placa}-{vigencias}.pdf</c>, <c>recibo-{placa}-consolidado.pdf</c> o <c>recibo-{placa}-paz-y-salvo.pdf</c>.</summary>
    internal static string NombreArchivo(string placa, Recibo recibo)
    {
        ArgumentNullException.ThrowIfNull(recibo);
        var sufijo = recibo.Tipo switch
        {
            TipoRecibo.Consolidado => "consolidado",
            TipoRecibo.PazYSalvo => "paz-y-salvo",
            _ => recibo.Periodos.Length > 0 ? string.Join('-', recibo.Periodos) : "vigencia",
        };
        return $"recibo-{placa}-{sufijo}.pdf";
    }
}

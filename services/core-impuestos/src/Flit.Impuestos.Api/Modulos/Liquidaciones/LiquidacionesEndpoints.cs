using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.Extensions.Primitives;

namespace Flit.Impuestos.Api.Modulos.Liquidaciones;

/// <summary>
/// Rutas de liquidaciones de M09 (plan §5.1; impuestos.v1.yaml 1.1.0, deltas 2 y 4):
/// <list type="bullet">
///   <item><c>POST /liquidaciones/individual</c> (<c>impuestos.liquidaciones.crear</c>): <c>{placa}</c> ⇒ 201 <c>LiquidacionConItems</c>
///   (ver <see cref="CrearLiquidacionIndividual"/>). El Super Admin liquida para <c>?empresa</c>, obligatoria para él.</item>
///   <item><c>GET /liquidaciones</c> (<c>impuestos.liquidaciones.ver</c>): grid con <c>tipo, estado, desde, hasta, placa, page,
///   pageSize</c>; orden por <c>creadaEn</c> descendente; <c>itemId</c>, <c>estadoItem</c> y <c>placa</c> solo en la individual.</item>
///   <item><c>GET /liquidaciones/{id}</c> y <c>GET /liquidaciones/{id}/items</c> (<c>…ver</c>): cabecera con <c>configCopia</c> y
///   placas del lote; otra compañía ⇒ 404 <c>LIQUIDACION_NO_ENCONTRADA</c>.</item>
///   <item><c>GET /liquidaciones/{id}/items/{itemId}</c> (<c>…ver</c>, M12): detalle de la placa (esquema <c>ItemDetalle</c>).</item>
/// </list>
/// <c>?empresa</c> sigue la regla única de <see cref="EmpresaExplicita"/>: quien no es Super Admin y la envía recibe 403
/// <c>EMPRESA_SOLO_SUPER_ADMIN</c>; el Super Admin sin ella o con un valor inválido, 400 <c>EMPRESA_REQUERIDA</c>; con una compañía
/// sin Impuestos, 404 <c>EMPRESA_NO_ENCONTRADA</c>. Sus lecturas quedan auditadas. <see cref="EmpresaDeEscrituraAsync"/> y
/// <see cref="EmpresaDeLecturaAsync"/> son las que usan las rutas que suman M10/M12 (masiva, plantilla, relanzar, cerrar).
/// <para>
/// Punto de extensión: <see cref="Map"/> devuelve el grupo <c>/liquidaciones</c>; los módulos siguientes mapean sobre él (p. ej.
/// <c>grupo.MapPost("/{id:guid}/items/{itemId:guid}/relanzar", …)</c>) desde su propio archivo, sin tocar este. Las rutas con id usan
/// <c>:guid</c> para no tapar <c>/plantilla</c> ni <c>/masiva</c>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var liquidaciones = LiquidacionesEndpoints.Map(api);
/// RelanzarItem.Map(liquidaciones); // M10
/// </code>
/// </example>
internal static class LiquidacionesEndpoints
{
    public const string Ruta = "/liquidaciones";
    public const string Recurso = "liquidaciones";
    public const int PageSizePorDefecto = 50;
    public const int PageSizeMaximo = 200;

    public static IServiceCollection AddLiquidacionesImpuestos(this IServiceCollection services)
    {
        services.AddScoped<ConsultaLiquidaciones>();
        services.AddScoped<CrearLiquidacionIndividual>();
        // M12: masiva, cierre manual, terminación del lote con su aviso (correo y pantalla).
        services.AddScoped<CrearLiquidacionMasiva>();
        services.AddScoped<CerrarLiquidacion>();
        services.AddScoped<EvaluadorTerminacion>();
        services.AddScoped<Avisos.AvisoLoteTerminado>();
        return services;
    }

    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var grupo = api.MapGroup(Ruta);
        grupo.MapGet(string.Empty, ListarAsync).RequirePermiso(Permisos.LiquidacionesVer).WithName("ListarLiquidaciones");
        grupo.MapPost("/individual", CrearIndividualAsync).RequirePermiso(Permisos.LiquidacionesCrear).CuerpoAcotado().WithName("CrearLiquidacionIndividual");
        grupo.MapGet("/{id:guid}", ObtenerAsync).RequirePermiso(Permisos.LiquidacionesVer).WithName("ObtenerLiquidacion");
        grupo.MapGet("/{id:guid}/items", ItemsAsync).RequirePermiso(Permisos.LiquidacionesVer).WithName("ListarItemsLiquidacion");
        grupo.MapGet("/{id:guid}/items/{itemId:guid}", ItemDetalleAsync).RequirePermiso(Permisos.LiquidacionesVer).WithName("ObtenerItemLiquidacion");
        return grupo;
    }

    /// <summary>
    /// Compañía de una escritura con <c>?empresa</c> (delta 2): la del token para quien no es Super Admin y no la envía; para el Super
    /// Admin, la de <c>?empresa</c> (obligatoria) y con Impuestos. Devuelve también su referencia para la respuesta.
    /// </summary>
    public static async Task<(Guid Empresa, ReferenciaDto Referencia)> EmpresaDeEscrituraAsync(
        StringValues consulta, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentNullException.ThrowIfNull(configuracion);
        if (EmpresaExplicita.Opcional(usuario.EsSuperAdmin, consulta) is not { } elegida)
            return Propia(usuario);
        await configuracion.ExigirConImpuestosAsync(elegida, ct).ConfigureAwait(false);
        return (elegida, await ReferenciaAsync(elegida, ambito, ct).ConfigureAwait(false));
    }

    /// <summary>Como <see cref="EmpresaDeEscrituraAsync"/>, y la lectura del Super Admin queda auditada antes de leer.</summary>
    public static async Task<(Guid Empresa, ReferenciaDto Referencia)> EmpresaDeLecturaAsync(
        StringValues consulta, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, AuditoriaLecturaBackoffice auditoria,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(auditoria);
        var resultado = await EmpresaDeEscrituraAsync(consulta, usuario, configuracion, ambito, ct).ConfigureAwait(false);
        if (usuario.EsSuperAdmin)
            await auditoria.RegistrarAsync(resultado.Empresa, Recurso, ct).ConfigureAwait(false);
        return resultado;
    }

    private static async Task<IResult> CrearIndividualAsync(
        HttpContext http, SolicitudIndividualEntrada? entrada, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito,
        CrearLiquidacionIndividual crear, CancellationToken ct)
    {
        var (empresa, referencia) = await EmpresaDeEscrituraAsync(http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, ct).ConfigureAwait(false);
        var creada = await crear.CrearAsync(empresa, entrada?.Placa, usuario.Id, referencia, ct, actorNombre: usuario.Nombre).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}{Ruta}/{creada.Id}", creada);
    }

    private static async Task<IResult> ListarAsync(
        HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, AuditoriaLecturaBackoffice auditoria,
        ConsultaLiquidaciones consulta, TimeProvider reloj, CancellationToken ct)
    {
        var query = http.Request.Query;
        // La regla de ?empresa va primero: 403 EMPRESA_SOLO_SUPER_ADMIN antes que cualquier 400.
        EmpresaExplicita.Opcional(usuario.EsSuperAdmin, query[EmpresaExplicita.Parametro]);
        var filtros = new FiltrosLiquidaciones(
            Enumerado<TipoLiquidacion>(query, "tipo"), Enumerado<EstadoLote>(query, "estado"), Rango(query, reloj), PlacaFiltro(query),
            Entero(query, "page", 1, 1, int.MaxValue), Entero(query, "pageSize", PageSizePorDefecto, 1, PageSizeMaximo));
        var (empresa, referencia) = await EmpresaDeLecturaAsync(query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, auditoria, ct).ConfigureAwait(false);
        return Results.Ok(await consulta.ListarAsync(empresa, filtros, referencia, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> ObtenerAsync(
        Guid id, HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, AuditoriaLecturaBackoffice auditoria,
        ConsultaLiquidaciones consulta, CancellationToken ct)
    {
        var (empresa, referencia) = await EmpresaDeLecturaAsync(http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, auditoria, ct).ConfigureAwait(false);
        return Results.Ok(await consulta.LiquidacionAsync(empresa, id, referencia, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> ItemsAsync(
        Guid id, HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, AuditoriaLecturaBackoffice auditoria,
        ConsultaLiquidaciones consulta, CancellationToken ct)
    {
        var query = http.Request.Query;
        EmpresaExplicita.Opcional(usuario.EsSuperAdmin, query[EmpresaExplicita.Parametro]);
        var estado = Enumerado<EstadoItem>(query, "estado");
        var placa = PlacaFiltro(query);
        var page = Entero(query, "page", 1, 1, int.MaxValue);
        var pageSize = Entero(query, "pageSize", PageSizePorDefecto, 1, PageSizeMaximo);
        var (empresa, _) = await EmpresaDeLecturaAsync(query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, auditoria, ct).ConfigureAwait(false);
        return Results.Ok(await consulta.ItemsAsync(empresa, id, estado, placa, page, pageSize, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> ItemDetalleAsync(
        Guid id, Guid itemId, HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito,
        AuditoriaLecturaBackoffice auditoria, ConsultaLiquidaciones consulta, CancellationToken ct)
    {
        var (empresa, referencia) = await EmpresaDeLecturaAsync(http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, auditoria, ct).ConfigureAwait(false);
        return Results.Ok(await consulta.ItemDetalleAsync(empresa, id, itemId, referencia, ct).ConfigureAwait(false));
    }

    private static (Guid, ReferenciaDto) Propia(UsuarioActual usuario)
    {
        var empresa = usuario.EmpresaPropia();
        return (empresa, new ReferenciaDto(empresa, usuario.EmpresaNombre ?? string.Empty));
    }

    private static async Task<ReferenciaDto> ReferenciaAsync(Guid empresa, AmbitoDeLaEmpresa ambito, CancellationToken ct)
    {
        var identificada = await ambito.IdentificarAsync(empresa, ct).ConfigureAwait(false);
        return new ReferenciaDto(identificada.Id, identificada.Nombre);
    }

    private static RangoDias Rango(IQueryCollection query, TimeProvider reloj)
    {
        try
        {
            return RangoDias.Leer(query, RangoDias.Hoy(reloj));
        }
        catch (ReglaException ex)
        {
            throw ErroresLiquidaciones.ConLiteral(ex);
        }
    }

    /// <summary>Un valor de enum por su código del contrato (<c>EN_PROCESO</c>); 400 <c>FILTRO_INVALIDO</c> si no lo es o se repite.</summary>
    private static T? Enumerado<T>(IQueryCollection query, string campo)
        where T : struct, Enum
    {
        var valores = query[campo];
        if (valores.Count == 0 || string.IsNullOrEmpty(valores[0]))
            return null;
        if (valores.Count > 1)
            throw ErroresLiquidaciones.Filtro(campo);
        foreach (var v in Enum.GetValues<T>())
        {
            if (string.Equals(CodigosDeEnum.Codigo(v), valores[0], StringComparison.Ordinal))
                return v;
        }

        throw ErroresLiquidaciones.Filtro(campo);
    }

    /// <summary>La placa (o parte) normalizada como al crear; 400 <c>FILTRO_INVALIDO</c> si no lo es.</summary>
    private static string? PlacaFiltro(IQueryCollection query)
    {
        var valores = query["placa"];
        if (valores.Count == 0 || string.IsNullOrWhiteSpace(valores[0]))
            return null;
        return valores.Count == 1 && Placa.TryNormalizar(valores[0], out var placa) ? placa : throw ErroresLiquidaciones.Filtro("placa");
    }

    private static int Entero(IQueryCollection query, string campo, int porDefecto, int minimo, int maximo)
    {
        var valores = query[campo];
        if (valores.Count == 0 || string.IsNullOrEmpty(valores[0]))
            return porDefecto;
        return valores.Count == 1 && int.TryParse(valores[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            && n >= minimo && n <= maximo
            ? n
            : throw ErroresLiquidaciones.Paginacion(campo);
    }
}

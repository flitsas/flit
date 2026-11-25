using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Estadisticas;

/// <summary>
/// <c>GET /dashboard</c> (plan §5.1, M14; impuestos.v1.yaml 1.1.0) con <c>impuestos.dashboard.ver</c>: <c>desde</c>/<c>hasta</c> (30 días
/// por defecto; invertido, futuro o de más de 366 días ⇒ 400 <c>RANGO_INVALIDO</c>, fecha mal escrita ⇒ 400 <c>FILTRO_INVALIDO</c>,
/// con el literal del contrato) y <c>empresa</c> (regla de <see cref="FiltroEstadisticas"/>). El Super Admin sin <c>empresa</c> ve
/// la vista global con <c>ranking</c>. Sin datos personales y sin caché en el cliente.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddEstadisticasYReportes();
/// EstadisticasEndpoints.Map(api); // api = app.MapGroup(Program.Prefijo).RequireAuthorization()
/// </code>
/// </example>
internal static class EstadisticasEndpoints
{
    public const string Ruta = "/dashboard";
    public const string Recurso = "dashboard";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(Ruta, ObtenerAsync).RequirePermiso(Permisos.DashboardVer).SinCacheEnRespuesta().WithName("ObtenerDashboardImpuestos");
    }

    private static async Task<IResult> ObtenerAsync(
        HttpContext http, UsuarioActual usuario, FiltroEstadisticas filtro, AlmacenCredenciales almacen, LecturaEntreEmpresas lectura, TimeProvider reloj,
        CancellationToken ct)
    {
        var query = http.Request.Query;
        var empresa = query[EmpresaExplicita.Parametro];
        FiltroEstadisticas.ValidarEmpresa(usuario, empresa);
        var rango = FiltroEstadisticas.Rango(query, reloj);
        var alcance = await filtro.ResolverAsync(empresa, Recurso, ct).ConfigureAwait(false);
        await using var fuente = FuenteLiquidaciones.Abrir(alcance, almacen, lectura);
        return Results.Ok(await CalculadoraEstadisticas.ObtenerAsync(fuente, alcance, rango, ct).ConfigureAwait(false));
    }
}

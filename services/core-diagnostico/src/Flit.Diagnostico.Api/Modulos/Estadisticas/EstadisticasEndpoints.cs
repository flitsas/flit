using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>
/// E1 <c>GET /estadisticas</c> (HU #14579, BE-1; contrato-api §1, SP-5, SP-6/O9, SP-11, SP-16): estadísticas de la compañía del token
/// con <c>diagnostico.diagnosticos.ver</c>, filtro de tenant normal (el SuperAdmin solo ve la suya) y la visibilidad del lote. No acepta
/// <c>?empresa=</c> (400) y no se audita: no lleva PII ni lee entre compañías. <c>Cache-Control: no-store</c>.
/// </summary>
internal static class EstadisticasEndpoints
{
    public const string Ruta = "/estadisticas";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(Ruta, CompaniaAsync).RequirePermiso(Permisos.DiagnosticosVer).SinCacheEnRespuesta().WithName("ObtenerEstadisticasCompania");
    }

    private static async Task<IResult> CompaniaAsync(
        HttpRequest request, DiagnosticoDb db, UsuarioActual usuario, TimeProvider tiempo, IConfiguration configuracion, CancellationToken ct)
    {
        var filtro = FiltroEstadisticas.Leer(request.Query, tiempo, admiteEmpresa: false);
        var empresa = usuario.EmpresaPropia();
        EstadisticasRegistro.AcotarTiempo(db, configuracion);
        var fuentes = await FuentesEstadisticas.DeLaCompaniaAsync(db, usuario, ct).ConfigureAwait(false);
        var lecturas = await CalculadoraEstadisticas.LeerAsync(fuentes, filtro, ct).ConfigureAwait(false);
        return Results.Ok(CalculadoraEstadisticas.Armar(new AmbitoEstadisticasDto("EMPRESA", empresa, usuario.EmpresaNombre), filtro, tiempo.GetUtcNow(), lecturas, []));
    }
}

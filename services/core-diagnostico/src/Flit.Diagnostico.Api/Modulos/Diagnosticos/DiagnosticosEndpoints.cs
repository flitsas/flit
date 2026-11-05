using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Diagnosticos;

/// <summary>
/// <c>POST /diagnosticos</c> y <c>PUT /diagnosticos/{id}</c> (HU #13750, F01-H4, plan §6.1): crear y editar la cabecera del
/// diagnóstico de la compañía del token. Permiso <c>diagnostico.diagnosticos.crear</c> en ambas; el backoffice FLIT no lo
/// tiene (R-6) y recibe 403. La edición solo procede en CREADO (409 fuera de él) y una cabecera de otra compañía responde
/// 404 (filtro por compañía de #13715).
/// <c>GET /diagnosticos?desde=&amp;hasta=&amp;modo=&amp;estado=&amp;nombre=&amp;pagina=&amp;tamano=</c> (HU #13845, F08-H1), permiso
/// <c>diagnostico.diagnosticos.ver</c>: lotes de la compañía del token visibles para el usuario (<see cref="ListadoDiagnosticos"/>).
/// </summary>
internal static class DiagnosticosEndpoints
{
    public const string Ruta = "/diagnosticos";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost(Ruta, Crear).RequirePermiso(Permisos.DiagnosticosCrear).WithName("CrearDiagnostico");
        api.MapPut(Ruta + "/{id:guid}", Editar).RequirePermiso(Permisos.DiagnosticosCrear).WithName("EditarDiagnostico");
        api.MapGet(Ruta, Listar).RequirePermiso(Permisos.DiagnosticosVer).WithName("ListarDiagnosticos");
    }

    public static IServiceCollection AddListadoDiagnosticos(this IServiceCollection services)
    {
        services.AddScoped<ListadoDiagnosticos>();
        return services;
    }

    private static async Task<IResult> Listar(
        string? desde, string? hasta, string? modo, string? estado, string? nombre, string? pagina, string? tamano,
        ListadoDiagnosticos listado, CancellationToken ct) =>
        Results.Ok(await listado.ListarAsync(FiltroListado.Leer(desde, hasta, modo, estado, nombre, pagina, tamano), ct).ConfigureAwait(false));

    private static async Task<IResult> Crear(CabeceraEntradaDto cuerpo, ServicioCabecera servicio, CancellationToken ct) =>
        Results.Json(DiagnosticoDto.De(await servicio.CrearAsync(cuerpo, ct).ConfigureAwait(false)), statusCode: StatusCodes.Status201Created);

    private static async Task<IResult> Editar(Guid id, CabeceraEntradaDto cuerpo, ServicioCabecera servicio, CancellationToken ct) =>
        Results.Ok(DiagnosticoDto.De(await servicio.EditarAsync(id, cuerpo, ct).ConfigureAwait(false)));
}

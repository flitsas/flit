using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Diagnosticos;

/// <summary>
/// <c>POST /diagnosticos</c> y <c>PUT /diagnosticos/{id}</c> (HU #13750, F01-H4, plan §6.1): crear y editar la cabecera del
/// diagnóstico de la compañía del token. Permiso <c>diagnostico.diagnosticos.crear</c> en ambas; el backoffice FLIT no lo
/// tiene (R-6) y recibe 403. La edición solo procede en CREADO (409 fuera de él) y una cabecera de otra compañía responde
/// 404 (filtro por compañía de #13715). No agrega lecturas: el listado y el detalle son de F07.
/// </summary>
internal static class DiagnosticosEndpoints
{
    public const string Ruta = "/diagnosticos";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost(Ruta, Crear).RequirePermiso(Permisos.DiagnosticosCrear).WithName("CrearDiagnostico");
        api.MapPut(Ruta + "/{id:guid}", Editar).RequirePermiso(Permisos.DiagnosticosCrear).WithName("EditarDiagnostico");
    }

    private static async Task<IResult> Crear(CabeceraEntradaDto cuerpo, ServicioCabecera servicio, CancellationToken ct) =>
        Results.Json(DiagnosticoDto.De(await servicio.CrearAsync(cuerpo, ct).ConfigureAwait(false)), statusCode: StatusCodes.Status201Created);

    private static async Task<IResult> Editar(Guid id, CabeceraEntradaDto cuerpo, ServicioCabecera servicio, CancellationToken ct) =>
        Results.Ok(DiagnosticoDto.De(await servicio.EditarAsync(id, cuerpo, ct).ConfigureAwait(false)));
}

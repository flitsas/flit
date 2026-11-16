using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Express;

/// <summary>
/// <c>POST /express</c> y <c>POST /express/estimado</c> (HU #13753, F02-H6, plan §6.1 fila 02; el estimado es nuevo frente al
/// plan), con el permiso <c>diagnostico.diagnosticos.crear</c>: el backoffice FLIT no lo tiene (R-6) y recibe 403. El
/// Express responde 201 con el id de la cabecera; el estimado responde 200 y no persiste nada. Las reglas viven en
/// <see cref="ServicioExpress"/>.
/// </summary>
internal static class ExpressEndpoints
{
    public const string Ruta = "/express";
    public const string RutaEstimado = Ruta + "/estimado";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost(Ruta, Crear).RequirePermiso(Permisos.DiagnosticosCrear).WithName("CrearDiagnosticoExpress");
        api.MapPost(RutaEstimado, Estimar).RequirePermiso(Permisos.DiagnosticosCrear).WithName("EstimarDiagnosticoExpress");
    }

    private static async Task<IResult> Crear(ExpressEntradaDto cuerpo, ServicioExpress servicio, CancellationToken ct)
    {
        var (cabecera, confirmacion) = await servicio.CrearAsync(cuerpo, ct).ConfigureAwait(false);
        var vehiculo = confirmacion.Vehiculos.Single();
        var validado = confirmacion.Resultado.Validos.Single();
        var vehiculoDto = new VehiculoConfirmadoDto(
            vehiculo.Id, vehiculo.Orden, validado.IdentificadorOriginal, validado.IdentificadorNorm, CodigosDeEnum.Codigo(validado.Tipo),
            validado.TipoDocumentoLocatario, validado.DocumentoLocatario is null ? null : AnalizadorCarga.Enmascarar(validado.DocumentoLocatario));
        var estimado = confirmacion.MostrarEstimado
            ? confirmacion.Resultado.Estimado.Select(EstimadoFuenteDto.De).ToList()
            : null;
        return Results.Json(
            new ExpressCreadoDto(
                cabecera.Id, cabecera.Nombre, CodigosDeEnum.Codigo(cabecera.Modo), cabecera.EsExpress, CodigosDeEnum.Codigo(cabecera.Estado),
                cabecera.CreadoEn, vehiculoDto, estimado),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Estimar(ExpressEntradaDto cuerpo, ServicioExpress servicio, CancellationToken ct) =>
        Results.Ok(await servicio.EstimarAsync(cuerpo, ct).ConfigureAwait(false));
}

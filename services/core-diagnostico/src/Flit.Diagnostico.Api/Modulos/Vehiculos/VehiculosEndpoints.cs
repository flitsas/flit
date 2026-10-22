using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Vehiculos;

/// <summary>
/// <c>POST /diagnosticos/{id}/carga/validar-filas</c> y <c>POST /diagnosticos/{id}/vehiculos</c> (HU #13751, F02-H5, plan
/// §6.1, MOD02), con el permiso <c>diagnostico.diagnosticos.crear</c> y solo con la cabecera en CREADO (409); una cabecera
/// de otra compañía responde 404. validar-filas revalida las filas corregidas con el mismo análisis de la carga
/// (<see cref="AnalizadorCarga"/>) y devuelve el mismo resumen sin escribir nada. La confirmación recibe un multipart con el
/// MISMO archivo del análisis (<see cref="CargaEndpoints.CampoArchivo"/>, mismas reglas y topes: 413/415/422) y el JSON de
/// <see cref="LoteDesdeArchivo.CampoCorrecciones"/> (decisión del usuario 2026-10-21 «Reenviar el archivo»: el análisis
/// enmascara el documento del locatario y solo el archivo lo trae completo, R-3); el lote lo arma
/// <see cref="LoteDesdeArchivo"/> y lo confirma <see cref="ServicioConfirmacionVehiculos"/>; responde 202 porque los vehículos
/// quedan encolados en el motor de consultas y el lote pasa a EN_PROCESO (HU #13727). El documento nunca vuelve
/// completo en la respuesta. Los logs solo llevan conteos.
/// </summary>
internal static partial class VehiculosEndpoints
{
    public const string RutaValidarFilas = DiagnosticosEndpoints.Ruta + "/{id:guid}/carga/validar-filas";
    public const string RutaVehiculos = DiagnosticosEndpoints.Ruta + "/{id:guid}/vehiculos";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost(RutaValidarFilas, ValidarFilas).RequirePermiso(Permisos.DiagnosticosCrear).WithName("ValidarFilasCargaDiagnostico");
        api.MapPost(RutaVehiculos, Confirmar).RequirePermiso(Permisos.DiagnosticosCrear).DisableAntiforgery().WithName("ConfirmarVehiculosDiagnostico");
    }

    private static async Task<IResult> ValidarFilas(
        Guid id, FilasAValidarDto cuerpo, DiagnosticoDb db, UsuarioActual usuario, ILoggerFactory logs, CancellationToken ct)
    {
        usuario.EmpresaPropia();
        var cabecera = await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El diagnóstico");
        if (cabecera.Estado != EstadoDiagnostico.Creado)
            throw ReglaException.Conflicto(ServicioCabecera.CodigoNoEditable, "El diagnóstico ya no está en CREADO: no admite revalidar filas.");

        // Las filas llegan como las de la plantilla; sin número de fila, su posición en la hoja (encabezado en la fila 1).
        var filas = cuerpo?.Filas ?? [];
        var tabla = new TablaCarga(
            [PlantillaCarga.ColumnaIdentificador, PlantillaCarga.ColumnaTipoDocumento, PlantillaCarga.ColumnaDocumento],
            [.. filas.Select((f, i) => new FilaLeida(f?.Fila ?? i + 2, [f?.Identificador, f?.TipoDocumentoLocatario, f?.DocumentoLocatario]))]);
        var resumen = AnalizadorCarga.Analizar(tabla, CargaEndpoints.Parametros(cabecera));
        var registro = logs.CreateLogger(typeof(VehiculosEndpoints).FullName!);
        FilasRevalidadas(registro, cabecera.Id, resumen.TotalFilas, resumen.Validos, resumen.Erroneos);
        return Results.Ok(resumen);
    }

    private static async Task<IResult> Confirmar(
        Guid id, HttpRequest peticion, DiagnosticoDb db, UsuarioActual usuario, IConfiguration configuracion, ServicioConfirmacionVehiculos servicio,
        CancellationToken ct)
    {
        // 404 y 409 antes de leer el archivo, como la carga; el servicio lo vuelve a comprobar dentro de su transacción.
        usuario.EmpresaPropia();
        var cabecera = await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El diagnóstico");
        if (cabecera.Estado != EstadoDiagnostico.Creado)
            throw ReglaException.Conflicto(ServicioCabecera.CodigoNoEditable, "El diagnóstico ya no está en CREADO: no admite confirmar vehículos.");

        var (tabla, formulario) = await CargaEndpoints.LeerArchivoAsync(peticion, configuracion, ct).ConfigureAwait(false);
        var correcciones = formulario[LoteDesdeArchivo.CampoCorrecciones];
        if (correcciones.Count > 1)
        {
            throw new ReglaException(LoteDesdeArchivo.CodigoCorreccionesInvalidas, "Envíe las correcciones en un solo campo «correcciones».")
            {
                Campo = LoteDesdeArchivo.CampoCorrecciones,
            };
        }

        var capturados = LoteDesdeArchivo.Capturados(tabla, LoteDesdeArchivo.LeerCorrecciones(correcciones.ToString()));
        var confirmacion = await servicio.ConfirmarAsync(id, capturados, ct).ConfigureAwait(false);
        var resultado = confirmacion.Resultado;
        var porOrden = confirmacion.Vehiculos.ToDictionary(v => v.Orden);
        var vehiculos = resultado.Validos.Select(v => new VehiculoConfirmadoDto(
            porOrden[v.Orden].Id, v.Orden, v.IdentificadorOriginal, v.IdentificadorNorm, CodigosDeEnum.Codigo(v.Tipo), v.TipoDocumentoLocatario,
            v.DocumentoLocatario is null ? null : AnalizadorCarga.Enmascarar(v.DocumentoLocatario))).ToList();
        var estimado = confirmacion.MostrarEstimado
            ? resultado.Estimado.Select(EstimadoFuenteDto.De).ToList()
            : null;
        return Results.Json(
            new ResultadoConfirmacionDto(
                confirmacion.DiagnosticoId, vehiculos.Count, resultado.Invalidos, resultado.Duplicados, resultado.LimiteVehiculosLote, vehiculos, estimado),
            statusCode: StatusCodes.Status202Accepted);
    }

    [LoggerMessage(EventId = 13752, Level = LogLevel.Information,
        Message = "Filas revalidadas del diagnóstico {DiagnosticoId}: {Filas} filas, {Validas} válidas, {Erroneas} erróneas")]
    private static partial void FilasRevalidadas(ILogger logger, Guid diagnosticoId, int filas, int validas, int erroneas);
}

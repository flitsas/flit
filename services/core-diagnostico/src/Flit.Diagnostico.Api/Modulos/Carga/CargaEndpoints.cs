using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Carga;

/// <summary>
/// <c>GET /plantilla</c> y <c>POST /diagnosticos/{id}/carga</c> (HU #13749, F02-H4, plan §6.1, MOD02), con el permiso
/// <c>diagnostico.diagnosticos.crear</c>. La carga recibe un multipart con el campo <see cref="CampoArchivo"/> y devuelve el
/// <see cref="ResumenValidacionDto"/> sin guardar filas ni llamar a fuentes externas: el lote se confirma en F02-H5. Solo
/// en CREADO (409 <see cref="ServicioCabecera.CodigoNoEditable"/>); una cabecera de otra compañía responde 404. El análisis
/// usa la copia de configuración de la cabecera (límite y estimado), no la de hoy. Tope de tamaño configurable en
/// <see cref="ClaveTamanoMaximo"/> (413). Los logs solo llevan conteos: el contenido de las filas nunca se escribe.
/// </summary>
internal static partial class CargaEndpoints
{
    public const string RutaPlantilla = "/plantilla";
    public const string RutaCarga = DiagnosticosEndpoints.Ruta + "/{id:guid}/carga";
    public const string CampoArchivo = "archivo";
    public const string ClaveTamanoMaximo = "Diagnostico:Carga:TamanoMaximoBytes";
    public const long TamanoMaximoPorDefecto = 5L * 1024 * 1024;
    public const string CodigoArchivoRequerido = "ARCHIVO_REQUERIDO";
    public const string CodigoArchivoDemasiadoGrande = "ARCHIVO_DEMASIADO_GRANDE";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet(RutaPlantilla, () => Results.File(PlantillaCarga.Generar(), PlantillaCarga.ContentType, PlantillaCarga.NombreArchivo))
            .RequirePermiso(Permisos.DiagnosticosCrear).WithName("DescargarPlantillaDiagnostico");
        api.MapPost(RutaCarga, Analizar).RequirePermiso(Permisos.DiagnosticosCrear).DisableAntiforgery().WithName("AnalizarCargaDiagnostico");
    }

    private static async Task<IResult> Analizar(
        Guid id, HttpRequest peticion, DiagnosticoDb db, UsuarioActual usuario, IConfiguration configuracion, ILoggerFactory logs, CancellationToken ct)
    {
        usuario.EmpresaPropia();
        var cabecera = await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El diagnóstico");
        if (cabecera.Estado != EstadoDiagnostico.Creado)
            throw ReglaException.Conflicto(ServicioCabecera.CodigoNoEditable, "El diagnóstico ya no está en CREADO: no admite una carga nueva.");

        var archivo = peticion.HasFormContentType ? (await peticion.ReadFormAsync(ct).ConfigureAwait(false)).Files.GetFile(CampoArchivo) : null;
        if (archivo is null || archivo.Length == 0)
            throw new ReglaException(CodigoArchivoRequerido, $"Adjunte el archivo de carga (.xlsx o .csv) en el campo «{CampoArchivo}».") { Campo = CampoArchivo };
        var maximo = configuracion.GetValue(ClaveTamanoMaximo, TamanoMaximoPorDefecto);
        if (archivo.Length > maximo)
        {
            throw new ReglaException(CodigoArchivoDemasiadoGrande,
                $"El archivo pesa más de {maximo / 1024} KB: divídalo en varios archivos y vuelva a subirlos.", StatusCodes.Status413PayloadTooLarge);
        }

        byte[] contenido;
        await using (var flujo = archivo.OpenReadStream())
        using (var copia = new MemoryStream((int)archivo.Length))
        {
            await flujo.CopyToAsync(copia, ct).ConfigureAwait(false);
            contenido = copia.ToArray();
        }

        var resumen = AnalizadorCarga.Analizar(FormatoCarga.Leer(archivo.FileName, contenido), Parametros(cabecera));
        var registro = logs.CreateLogger(typeof(CargaEndpoints).FullName!);
        CargaAnalizada(registro, cabecera.Id, resumen.TotalFilas, resumen.Validos, resumen.Erroneos, resumen.Duplicados);
        return Results.Ok(resumen);
    }

    /// <summary>Alcance de la cabecera y, de su copia de configuración, el límite por lote y si el estimado se muestra antes de lanzar.</summary>
    private static ParametrosAnalisis Parametros(CabeceraDiagnostico cabecera)
    {
        using var copia = JsonDocument.Parse(cabecera.Configuracion);
        var raiz = copia.RootElement;
        var limite = raiz.TryGetProperty(ServicioConfiguracion.CampoLimiteVehiculosLote, out var l) && l.TryGetInt32(out var valor)
            ? valor
            : new ConfiguracionEmpresa().LimiteVehiculosLote;
        var mostrar = raiz.TryGetProperty(ServicioConfiguracion.CampoEstimadoConsultas, out var e)
            && CodigosDeEnum.TryLeer<EstimadoConsultas>(e.GetString(), out var estimado)
            && estimado == EstimadoConsultas.AntesDeLanzar;
        return new ParametrosAnalisis(cabecera.Alcance, limite, mostrar);
    }

    [LoggerMessage(EventId = 13749, Level = LogLevel.Information,
        Message = "Carga analizada del diagnóstico {DiagnosticoId}: {Filas} filas, {Validas} válidas, {Erroneas} erróneas, {Duplicadas} duplicadas")]
    private static partial void CargaAnalizada(ILogger logger, Guid diagnosticoId, int filas, int validas, int erroneas, int duplicadas);
}

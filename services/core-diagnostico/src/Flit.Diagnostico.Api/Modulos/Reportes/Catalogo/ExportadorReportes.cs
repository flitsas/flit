using System.Diagnostics;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Exportación de un reporte a Excel o PDF (HU #14579, BE-3; SP-10..SP-15; contrato-api §5), reutilizable por la compañía (R2/R3) y por
/// el backoffice (BE-4: B2/B3 con <c>global: true</c>, B5/B6): freno de concurrencia primero (429 <c>EXPORTACION_EN_CURSO</c> con
/// <c>Retry-After: 10</c>, sin registro), luego <see cref="MotorReportes.ArchivoAsync"/> (422 <c>REPORTE_DEMASIADO_GRANDE</c> con
/// <c>filas</c> y <c>maximo</c>, sin descifrar ni registrar) y el archivo con su nombre fijo. Las cabeceras de descarga segura las pone
/// la ruta (<c>.DescargaSegura()</c>). Al log solo tipo, formato, filas, empresas, duración y el id del registro.
/// </summary>
internal sealed class ExportadorReportes(MotorReportes motor, FrenoExportaciones freno, UsuarioActual usuario, TimeProvider reloj, ILogger<ExportadorReportes> logger)
{
    public const string CodigoEnCurso = "EXPORTACION_EN_CURSO";
    public const int RetryAfterSegundos = 10;

    public async Task<IResult> ExportarAsync(
        HttpResponse respuesta, ContextoReporte contexto, FiltroReporte filtro, FormatoReporte formato, AuditorReporte auditar, CancellationToken ct,
        bool global = false)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentNullException.ThrowIfNull(filtro);
        var tipo = TiposDeReporte.Codigo(filtro.Tipo);
        var codigoFormato = CodigosDeEnum.Codigo(formato);
        using var permiso = freno.Entrar(usuario.Id, out var motivo);
        if (permiso is null)
        {
            LogReportes.EnCurso(logger, tipo, codigoFormato, motivo ?? string.Empty);
            respuesta.Headers.RetryAfter = RetryAfterSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Json(new { code = CodigoEnCurso, error = "Ya hay una exportación en curso. Intenta de nuevo en unos segundos." },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var cronometro = Stopwatch.StartNew();
        ArchivoReporte archivo;
        try
        {
            archivo = await motor.ArchivoAsync(contexto, filtro, formato, auditar, reloj.GetUtcNow(), ct).ConfigureAwait(false);
        }
        catch (ReporteDemasiadoGrandeException ex)
        {
            LogReportes.DemasiadoGrande(logger, tipo, codigoFormato, ex.Filas, ex.Maximo);
            return Results.Json(new { code = ReporteDemasiadoGrandeException.Codigo, error = ex.Message, filas = ex.Filas, maximo = ex.Maximo },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var (bytes, tipoContenido) = formato == FormatoReporte.Xlsx
            ? (ExcelReporte.Generar(archivo), ExcelReporte.ContentType)
            : (PdfReporte.Generar(archivo), PdfReporte.ContentType);
        LogReportes.Entregado(logger, tipo, codigoFormato, archivo.Filas.Count, archivo.Empresas, cronometro.ElapsedMilliseconds, archivo.AccesoId);
        return Results.File(bytes, tipoContenido, NombreArchivoReporte.De(filtro, formato, global));
    }
}

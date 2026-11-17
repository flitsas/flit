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

        var (bytes, tipoContenido) = Generar(archivo, formato, tipo, codigoFormato, cronometro, ct);
        LogReportes.Entregado(logger, tipo, codigoFormato, archivo.Filas.Count, archivo.Empresas, cronometro.ElapsedMilliseconds, archivo.AccesoId);
        return Results.File(bytes, tipoContenido, NombreArchivoReporte.De(filtro, formato, global));
    }

    /// <summary>
    /// Genera el archivo en el hilo de la petición, cancelable por bloques (CR-BE-5): si el cliente se va, o si pasan
    /// <see cref="OpcionesReportes.TimeoutSegundos"/> generando, se corta en el siguiente bloque o página. El tiempo límite agotado es
    /// un <see cref="TimeoutException"/> (500, como cualquier fallo); el registro del acceso ya quedó escrito.
    /// </summary>
    private (byte[] Bytes, string TipoContenido) Generar(
        ArchivoReporte archivo, FormatoReporte formato, string tipo, string codigoFormato, Stopwatch cronometro, CancellationToken ct)
    {
        var limite = motor.Opciones.TimeoutSegundos;
        using var tiempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tiempo.CancelAfter(TimeSpan.FromSeconds(limite));
        try
        {
            return formato == FormatoReporte.Xlsx
                ? (ExcelReporte.Generar(archivo, tiempo.Token), ExcelReporte.ContentType)
                : (PdfReporte.Generar(archivo, tiempo.Token), PdfReporte.ContentType);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LogReportes.TiempoAgotado(logger, tipo, codigoFormato, archivo.Filas.Count, limite, cronometro.ElapsedMilliseconds, archivo.AccesoId);
            throw new TimeoutException("La generación del archivo superó el tiempo límite.");
        }
    }
}

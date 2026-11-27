namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Logs del módulo de reportes (HU #14579; SP-9): solo tipo, formato, filas, empresas, duración, el id del registro, el id interno de
/// una persona y el tipo de una excepción. Nunca placa, VIN, documento, nombre o NIT, ni el <c>Message</c> de una excepción.
/// </summary>
internal static partial class LogReportes
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Reporte {TipoReporte} {Formato} entregado: {Filas} filas, {Empresas} empresas, {DuracionMs} ms, registro {AccesoId}.")]
    public static partial void Entregado(ILogger logger, string tipoReporte, string formato, int filas, int empresas, long duracionMs, Guid accesoId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Documento no descifrable en el reporte {TipoReporte}: persona {PersonaId} ({Excepcion}); sale «No disponible».")]
    public static partial void NoDescifrado(ILogger logger, string tipoReporte, Guid? personaId, string excepcion);

    /// <summary>HU #14752 (SP-7): un documento del Excel del lote que no se pudo descifrar; solo el vehículo, el rol y el tipo de la excepción.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Documento no descifrable en el Excel del lote: vehículo {VehiculoId}, rol {Rol} ({Excepcion}); la celda sale vacía.")]
    public static partial void NoDescifradoExcel(ILogger logger, Guid vehiculoId, string rol, string excepcion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exportación de {TipoReporte} {Formato} rechazada: {Filas} filas pasan el máximo de {Maximo}.")]
    public static partial void DemasiadoGrande(ILogger logger, string tipoReporte, string formato, int filas, int maximo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exportación de {TipoReporte} {Formato} rechazada: ya hay una en curso ({Motivo}).")]
    public static partial void EnCurso(ILogger logger, string tipoReporte, string formato, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exportación de {TipoReporte} {Formato} cortada: {Filas} filas superaron el tiempo límite de {LimiteSegundos} s ({DuracionMs} ms), registro {AccesoId}.")]
    public static partial void TiempoAgotado(ILogger logger, string tipoReporte, string formato, int filas, int limiteSegundos, long duracionMs, Guid accesoId);
}

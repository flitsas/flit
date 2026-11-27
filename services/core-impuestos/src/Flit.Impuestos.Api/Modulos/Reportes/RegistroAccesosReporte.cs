using Flit.Impuestos.Api.Modulos.Estadisticas;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Reportes;

/// <summary>
/// Rastro de cada lectura y exportación de un reporte con datos del propietario (DA-7=b, Ley 1581): quién (id del usuario), qué
/// reporte, en qué formato, cuántas compañías y cuántas filas, en el log estructurado con el <c>CorrelationId</c> de la petición. Nunca
/// la placa, los filtros con valor, el propietario ni el contenido. Las lecturas del Super Admin sobre otra compañía, además, quedan en
/// <c>impuestos.auditoria_lectura_backoffice</c> (<see cref="Comun.AuditoriaLecturaBackoffice"/>). M14 no tiene tabla propia de accesos
/// (el carril no crea migraciones): si el gate de seguridad la pide, se suma con su migración y este punto la escribe.
/// </summary>
/// <example>
/// <code>
/// registro.Registrar(definicion, "excel", alcance, filas: 120);
/// </code>
/// </example>
internal sealed partial class RegistroAccesosReporte(UsuarioActual usuario, ILogger<RegistroAccesosReporte> logger)
{
    public void Registrar(DefinicionReporte definicion, string formato, AlcanceLectura alcance, int filas)
    {
        ArgumentNullException.ThrowIfNull(definicion);
        ArgumentNullException.ThrowIfNull(alcance);
        LogAcceso(logger, usuario.Id, definicion.Codigo, formato, alcance.Global, alcance.Empresas.Count, filas);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Reporte de impuestos leído: usuario {UsuarioId}, reporte {Reporte}, formato {Formato}, global {Global}, compañías {Companias}, filas {Filas}.")]
    private static partial void LogAcceso(ILogger logger, Guid usuarioId, string reporte, string formato, bool global, int companias, int filas);
}

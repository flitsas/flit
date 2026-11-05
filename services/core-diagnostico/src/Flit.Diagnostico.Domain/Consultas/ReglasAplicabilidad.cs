using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Lo que se sabe del vehículo tras completar su RUNT, para decidir qué celdas no aplican (HU #13733).
/// </summary>
/// <param name="Servicio">Servicio del vehículo según el RUNT (<c>PARTICULAR</c>, <c>PUBLICO</c>, ...); null si no lo trajo.</param>
/// <param name="DepartamentoCodigo">Departamento derivado del organismo; null si el organismo no está en la réplica.</param>
/// <param name="DepartamentoSoportado">Verdadero si <c>departamentos_impuesto</c> tiene el departamento con <c>soportado=true</c>.</param>
/// <param name="OrigenDocumentoLocatario">Origen del documento del locatario guardado; null si el vehículo no tiene ninguno.</param>
public sealed record HechosAplicabilidad(string? Servicio, string? DepartamentoCodigo, bool DepartamentoSoportado, OrigenDocumento? OrigenDocumentoLocatario);

/// <summary>
/// Reglas de no aplica (HU #13733, F03-C6/F03-C9, R-3, R-9), evaluadas al completar la RUNT del vehículo y antes de liberar sus
/// demás celdas. <see cref="Fuente.ImpuestoDepartamental"/>: con servicio público, <see cref="MotivoNa.ServicioPublico"/>
/// (manda sobre el departamento); con departamento nulo o sin fila soportada, <see cref="MotivoNa.DepartamentoNoSoportado"/>.
/// <see cref="Fuente.RuntLocatario"/> y <see cref="Fuente.SimitLocatario"/> sin documento del locatario (ni del proveedor ni
/// declarado): <see cref="MotivoNa.SinDocumentoLocatario"/>. Las demás fuentes siempre aplican. Una celda NA no genera
/// llamada ni consumo. Regla pura: no ve números de documento.
/// </summary>
/// <example>
/// <code>
/// var motivo = ReglasAplicabilidad.Evaluar(Fuente.ImpuestoDepartamental, new HechosAplicabilidad("PUBLICO", "05", true, null));
/// // MotivoNa.ServicioPublico
/// </code>
/// </example>
public static class ReglasAplicabilidad
{
    /// <summary>El motivo por el que la celda de <paramref name="fuente"/> no aplica, o null si aplica.</summary>
    public static MotivoNa? Evaluar(Fuente fuente, HechosAplicabilidad hechos)
    {
        ArgumentNullException.ThrowIfNull(hechos);
        return fuente switch
        {
            Fuente.ImpuestoDepartamental when EsServicioPublico(hechos.Servicio) => MotivoNa.ServicioPublico,
            Fuente.ImpuestoDepartamental when hechos.DepartamentoCodigo is null || !hechos.DepartamentoSoportado => MotivoNa.DepartamentoNoSoportado,
            Fuente.RuntLocatario or Fuente.SimitLocatario when hechos.OrigenDocumentoLocatario is null => MotivoNa.SinDocumentoLocatario,
            _ => null,
        };
    }

    /// <summary>Verdadero si el servicio del RUNT es público (sin importar mayúsculas, tildes ni espacios).</summary>
    public static bool EsServicioPublico(string? servicio) =>
        NombreNormalizado.Normalizar(servicio).Split(' ').Contains("PUBLICO", StringComparer.Ordinal);
}

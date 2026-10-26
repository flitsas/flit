using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Doble de prueba de core-consultas (ADR-0075, U3 = a): la simulación de LOCAL y DEV vive en core-consultas; en las pruebas
/// responde los trabajos de <see cref="BusDePrueba"/>. Lo que sigue es su comportamiento heredado de la fuente de Diagnóstico.
/// Puerta única del RUNT del vehículo (HU #13725, F03-C15): el producto no guarda credenciales ni llama a proveedores por su
/// cuenta. Devuelve un resultado normalizado (placa, VIN, servicio, clase, organismo, propietarios y locatarios con tipo y
/// número de documento, tipo de propiedad, servicio que resolvió e ID de transacción) o un error tipado. Nunca expone
/// nombres. Implementaciones, elegidas por <c>SeleccionAdaptadores</c>: <c>FuenteRuntReal</c> (con
/// <c>Diagnostico:Consultas:Address</c>: <c>RuntIntempoService</c> de core-consultas, HU #13878), <see cref="FuenteRuntIntempoSimulada"/>
/// (solo con <c>Diagnostico:Simulacion:Habilitada=true</c> en LOCAL o DEV) y <see cref="FuenteRuntNoConectada"/> (por defecto:
/// error definitivo, nunca datos).
/// </summary>
/// <example>
/// <code>
/// var resultado = await fuente.ConsultarVehiculoAsync(empresa, new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, "TST001"), ct);
/// if (resultado.Exitoso) { var propietario = resultado.Vehiculo!.Propietarios[0]; }
/// else if (resultado.EsTransitorio) { /* reintentar con backoff (D-12) */ }
/// </code>
/// </example>
public interface IFuenteRunt
{
    /// <param name="empresa">Compañía por la que se consulta (consumo y vigencia por compañía).</param>
    /// <param name="identificador">Placa o VIN normalizado.</param>
    /// <param name="ct">Cancelación.</param>
    Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct);
}

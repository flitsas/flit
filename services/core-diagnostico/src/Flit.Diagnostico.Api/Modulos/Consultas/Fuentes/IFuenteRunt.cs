using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>Lo que se le pide al RUNT del vehículo: una placa o un VIN ya normalizado por MOD02.</summary>
/// <param name="Tipo">Clase del identificador (placa de carro, moto, remolque, maquinaria o VIN).</param>
/// <param name="Valor">Identificador normalizado (mayúsculas, sin separadores).</param>
public sealed record IdentificadorVehiculo(TipoIdentificador Tipo, string Valor)
{
    public bool EsVin => Tipo == TipoIdentificador.Vin;
}

/// <summary>
/// Puerta única del RUNT del vehículo (HU #13725, F03-C15): el producto no guarda credenciales ni llama a proveedores por su
/// cuenta. Devuelve un resultado normalizado (placa, VIN, servicio, clase, organismo, propietarios y locatarios con tipo y
/// número de documento, tipo de propiedad, servicio que resolvió e ID de transacción) o un error tipado. Nunca expone
/// nombres. Implementaciones, elegidas por <see cref="SeleccionAdaptadores"/>: <see cref="FuenteRuntReal"/> (con
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

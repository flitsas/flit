namespace Flit.Diagnostico.Domain.Estimacion;

/// <summary>
/// Lo mínimo de un vehículo capturado que necesita <see cref="EstimadorConsultas"/> (HU #13747): si su identificador es válido
/// (<c>ResultadoValidacion.EsValido</c>), si trae documento de locatario declarado (R-3) y, si ya se conocen (al finalizar,
/// HU #13796), cuántas personas tiene registradas por rol.
/// </summary>
/// <param name="EsValido">Verdadero si el identificador es placa o VIN; un inválido nunca llega a una fuente.</param>
/// <param name="TieneDocumentoLocatario">Verdadero si la captura declara el documento del locatario.</param>
/// <param name="Personas">Personas registradas del vehículo; null si aún no se conocen (antes de lanzar o sin RUNT).</param>
public readonly record struct VehiculoAEstimar(bool EsValido, bool TieneDocumentoLocatario, PersonasRegistradas? Personas = null);

/// <summary>Personas registradas de un vehículo por rol (<c>personas_vehiculo</c>, HU #13790): propietarios y locatarios.</summary>
/// <param name="Propietarios">Filas con rol PROPIETARIO.</param>
/// <param name="Locatarios">Filas con rol LOCATARIO (incluye el declarado que entró como persona).</param>
public readonly record struct PersonasRegistradas(int Propietarios, int Locatarios);

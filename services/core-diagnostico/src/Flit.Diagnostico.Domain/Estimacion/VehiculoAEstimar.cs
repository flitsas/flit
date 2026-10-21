namespace Flit.Diagnostico.Domain.Estimacion;

/// <summary>
/// Lo mínimo de un vehículo capturado que necesita <see cref="EstimadorConsultas"/> (HU #13747): si su identificador es válido
/// (<c>ResultadoValidacion.EsValido</c>) y si trae documento de locatario declarado (R-3).
/// </summary>
/// <param name="EsValido">Verdadero si el identificador es placa o VIN; un inválido nunca llega a una fuente.</param>
/// <param name="TieneDocumentoLocatario">Verdadero si la captura declara el documento del locatario.</param>
public readonly record struct VehiculoAEstimar(bool EsValido, bool TieneDocumentoLocatario);

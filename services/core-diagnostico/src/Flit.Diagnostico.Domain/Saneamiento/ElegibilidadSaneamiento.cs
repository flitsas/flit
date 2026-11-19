using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;

namespace Flit.Diagnostico.Domain.Saneamiento;

/// <summary>
/// Elegibilidad para solicitar el saneamiento (plan §3.1, S3, S5, S26): el vehículo está terminado (<see cref="AvanceVehiculo.Terminado"/>,
/// la misma condición de «Crear trámite») y no tiene un saneamiento no anulado. No dependen de ella el modo, el veredicto, el estado del
/// lote ni el trámite. Con un saneamiento no anulado el motivo es <see cref="YaSolicitado"/> aunque el vehículo haya dejado de estar
/// terminado (un reproceso no lo cambia, S15).
/// </summary>
/// <example>
/// <code>
/// ElegibilidadSaneamiento.MotivoNoElegible([EstadoCelda.Completado, EstadoCelda.Error], null); // NO_TERMINADO
/// </code>
/// </example>
public static class ElegibilidadSaneamiento
{
    public const string NoTerminado = "NO_TERMINADO";
    public const string YaSolicitado = "YA_SOLICITADO";

    /// <summary>Null si es elegible. <paramref name="vigente"/>: estado del saneamiento no anulado del vehículo (null o ANULADO si no hay).</summary>
    public static string? MotivoNoElegible(bool terminado, EstadoSaneamiento? vigente)
    {
        if (vigente is { } estado && estado != EstadoSaneamiento.Anulado)
            return YaSolicitado;
        return terminado ? null : NoTerminado;
    }

    /// <summary>Lo mismo, con los estados de todas las celdas del vehículo.</summary>
    public static string? MotivoNoElegible(IEnumerable<EstadoCelda> celdas, EstadoSaneamiento? vigente) =>
        MotivoNoElegible(AvanceVehiculo.Terminado(celdas), vigente);
}

using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Cuándo se puede emitir el PDF de un vehículo (R-7, decisión Q7; regla única que reutilizan el PDF de F07-H3, el ZIP de F07-H5
/// y la grilla): si el vehículo terminó (<see cref="AvanceVehiculo.Terminado"/>: todas sus celdas en COMPLETADO o N/A), aunque el
/// lote siga EN_PROCESO; o, con el lote INCOMPLETO, si todas sus celdas están cerradas (COMPLETADO, N/A o ERROR: la fuente en
/// error sale «consulta no disponible»). En otro caso, 409 <see cref="CodigoVehiculoNoTerminado"/>.
/// </summary>
/// <example>
/// <code>
/// EmisionPdf.Exigir(cabecera, celdas.Select(c => c.Estado)); // 409 VEHICULO_NO_TERMINADO si no se puede
/// </code>
/// </example>
public static class EmisionPdf
{
    public const string CodigoVehiculoNoTerminado = "VEHICULO_NO_TERMINADO";

    public static bool Puede(EstadoDiagnostico estadoLote, IEnumerable<EstadoCelda> celdas)
    {
        ArgumentNullException.ThrowIfNull(celdas);
        var estados = celdas as IReadOnlyCollection<EstadoCelda> ?? [.. celdas];
        if (AvanceVehiculo.Terminado(estados))
            return true;
        return estadoLote == EstadoDiagnostico.Incompleto
            && estados.Count > 0
            && estados.All(e => e is EstadoCelda.Completado or EstadoCelda.Na or EstadoCelda.Error);
    }

    public static bool Puede(CabeceraDiagnostico cabecera, IEnumerable<EstadoCelda> celdas)
    {
        ArgumentNullException.ThrowIfNull(cabecera);
        return Puede(cabecera.Estado, celdas);
    }

    /// <summary><see cref="Puede(CabeceraDiagnostico, IEnumerable{EstadoCelda})"/> o 409 <see cref="CodigoVehiculoNoTerminado"/>.</summary>
    public static void Exigir(CabeceraDiagnostico cabecera, IEnumerable<EstadoCelda> celdas)
    {
        if (!Puede(cabecera, celdas))
            throw ReglaException.Conflicto(CodigoVehiculoNoTerminado, "El vehículo todavía tiene consultas sin terminar: su reporte sale cuando terminen.");
    }
}

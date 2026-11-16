using System.Diagnostics.Metrics;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Métricas propias de core-diagnostico (medidor <see cref="Medidor"/>). <see cref="TiposNoHomologados"/> (HU #14582, contrato §1.4,
/// CR-4): cuántos propietarios del RUNT traen un tipo de documento fuera de la tabla de sinónimos al fijar <c>cambio_propietario</c>.
/// La medición lleva solo el id del vehículo como etiqueta: nunca el tipo ni el número.
/// </summary>
/// <example>
/// <code>
/// MetricasDiagnostico.ContarTiposNoHomologados(vehiculo.Id, 1);
/// </code>
/// </example>
internal static class MetricasDiagnostico
{
    public const string Medidor = "Flit.Diagnostico";
    public const string TiposNoHomologados = "diagnostico.tipos_no_homologados";
    public const string EtiquetaVehiculo = "vehiculo_id";

    private static readonly Meter Instancia = new(Medidor);

    private static readonly Counter<long> ContadorTiposNoHomologados =
        Instancia.CreateCounter<long>(TiposNoHomologados, "{propietario}", "Propietarios del RUNT con un tipo de documento fuera de la tabla de sinónimos.");

    public static void ContarTiposNoHomologados(Guid vehiculoId, int cantidad)
    {
        if (cantidad > 0)
            ContadorTiposNoHomologados.Add(cantidad, new KeyValuePair<string, object?>(EtiquetaVehiculo, vehiculoId.ToString()));
    }
}

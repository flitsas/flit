using System.Diagnostics.Metrics;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Métricas propias de core-diagnostico (medidor <see cref="Medidor"/>). <see cref="TiposNoHomologados"/> (HU #14582, contrato §1.4,
/// CR-4): cuántos propietarios del RUNT traen un tipo de documento fuera de la tabla de sinónimos al fijar <c>cambio_propietario</c>.
/// La medición lleva una sola etiqueta, <see cref="EtiquetaOrigen"/> (<see cref="OrigenCierre"/>, <see cref="OrigenReutilizacion"/> o
/// <see cref="OrigenBackfill"/>): cardinalidad acotada, sin id de vehículo (CR-2 del review de be1-b), y nunca el tipo ni el número.
/// </summary>
/// <example>
/// <code>
/// MetricasDiagnostico.ContarTiposNoHomologados(MetricasDiagnostico.OrigenCierre, 1);
/// </code>
/// </example>
internal static class MetricasDiagnostico
{
    public const string Medidor = "Flit.Diagnostico";
    public const string TiposNoHomologados = "diagnostico.tipos_no_homologados";
    public const string EtiquetaOrigen = "origen";
    public const string OrigenCierre = "cierre";
    public const string OrigenReutilizacion = "reutilizacion";
    public const string OrigenBackfill = "backfill";

    private static readonly Meter Instancia = new(Medidor);

    private static readonly Counter<long> ContadorTiposNoHomologados =
        Instancia.CreateCounter<long>(TiposNoHomologados, "{propietario}", "Propietarios del RUNT con un tipo de documento fuera de la tabla de sinónimos.");

    public static void ContarTiposNoHomologados(string origen, int cantidad)
    {
        if (cantidad > 0)
            ContadorTiposNoHomologados.Add(cantidad, new KeyValuePair<string, object?>(EtiquetaOrigen, origen));
    }
}

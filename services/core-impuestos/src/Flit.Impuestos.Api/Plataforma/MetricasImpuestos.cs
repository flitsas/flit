using System.Diagnostics.Metrics;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Métricas propias de core-impuestos (medidor <see cref="Medidor"/>). <see cref="Alertas"/> cuenta las alertas operativas (por
/// ejemplo, un aprovisionamiento que pasa a <c>ERROR(AMBIENTE)</c> o a <c>REQUIERE_CREDENCIALES</c> por un 401). La medición lleva
/// una sola etiqueta, <see cref="EtiquetaMotivo"/>, con un código de cardinalidad acotada: nunca la compañía, la placa ni un dato
/// personal.
/// </summary>
/// <example>
/// <code>
/// MetricasImpuestos.ContarAlerta("aprovisionamiento_ambiente");
/// </code>
/// </example>
internal static class MetricasImpuestos
{
    public const string Medidor = "Flit.Impuestos";
    public const string Alertas = "impuestos.alertas";
    public const string EtiquetaMotivo = "motivo";

    private static readonly Meter Instancia = new(Medidor);

    private static readonly Counter<long> ContadorAlertas =
        Instancia.CreateCounter<long>(Alertas, "{alerta}", "Alertas operativas de Impuestos por motivo.");

    public static void ContarAlerta(string motivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        ContadorAlertas.Add(1, new KeyValuePair<string, object?>(EtiquetaMotivo, motivo));
    }
}

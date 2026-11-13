using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// HU #14563 B5 (CF-20, R-20, D6) — la auditoría de una consulta que ve la compañía en el detalle de la celda, armada de
/// <c>diagnostico.intentos_consulta</c>. Los intentos <see cref="DisparadorIntento.Manual"/> (llenado de soporte) se excluyen de la
/// lista y de los agregados: exponerlos revelaría el origen manual de la celda. Nunca viajan servicio, transacción, mensaje de
/// error, usuario, identificador ni persona: los tipos no los tienen.
/// </summary>
internal static class AuditoriaConsultaCliente
{
    /// <summary>La auditoría de los <paramref name="intentos"/> de una celda; null si no hay ninguno que no sea manual.</summary>
    public static AuditoriaConsulta? Desde(IEnumerable<IntentoConsulta> intentos)
    {
        ArgumentNullException.ThrowIfNull(intentos);
        var visibles = intentos.Where(i => i.Disparador != DisparadorIntento.Manual).OrderBy(i => i.Inicio).ThenBy(i => i.Id).ToList();
        if (visibles.Count == 0)
            return null;
        var detalle = visibles.Select((i, n) => new IntentoConsultaCliente(
            n + 1, i.Inicio, i.Fin, Milisegundos(i.Inicio, i.Fin),
            i.Estado == ResultadoIntento.Success ? "EXITOSO" : "ERROR",
            i.Disparador == DisparadorIntento.Reproceso ? "REPROCESO" : "AUTOMATICO",
            i.DesdeCache)).ToList();
        var inicio = visibles[0].Inicio;
        var fin = visibles[^1].Fin;
        return new AuditoriaConsulta(inicio, fin, Milisegundos(inicio, fin), detalle.Count, detalle);
    }

    private static long Milisegundos(DateTimeOffset inicio, DateTimeOffset fin) => Math.Max(0, (long)(fin - inicio).TotalMilliseconds);
}

/// <summary>Auditoría saneada de una consulta (HU #14563 B5): inicio, fin, duración y los intentos no manuales.</summary>
public sealed record AuditoriaConsulta(
    [property: JsonPropertyName("inicio")] DateTimeOffset Inicio,
    [property: JsonPropertyName("fin")] DateTimeOffset Fin,
    [property: JsonPropertyName("duracion_ms")] long DuracionMs,
    [property: JsonPropertyName("intentos")] int Intentos,
    [property: JsonPropertyName("intentos_detalle")] IReadOnlyList<IntentoConsultaCliente> IntentosDetalle);

/// <summary>Un intento visible a la compañía: resultado <c>EXITOSO</c>/<c>ERROR</c> y disparador <c>AUTOMATICO</c>/<c>REPROCESO</c>.</summary>
public sealed record IntentoConsultaCliente(
    [property: JsonPropertyName("n")] int N,
    [property: JsonPropertyName("inicio")] DateTimeOffset Inicio,
    [property: JsonPropertyName("fin")] DateTimeOffset Fin,
    [property: JsonPropertyName("duracion_ms")] long DuracionMs,
    [property: JsonPropertyName("resultado")] string Resultado,
    [property: JsonPropertyName("disparador")] string Disparador,
    [property: JsonPropertyName("desde_cache")] bool DesdeCache);

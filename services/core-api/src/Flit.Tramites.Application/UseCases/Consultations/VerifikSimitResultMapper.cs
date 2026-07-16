namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Mapper puro Verifik SIMIT → <see cref="ConsultationResult"/> normalizado.
/// Aplica para respuestas por documento (§3.5) y por placa (§3.6): misma forma.
/// Semáforo: multas pendientes → warn; acuerdos de pago activos → warn; sin deudas → ok.
/// Nunca lanza; robusto ante nulls.
///
/// FEATURE 05: los checks se construyen en <see cref="FinesCheckFactory"/>, compartida con los
/// demás proveedores de comparendos, para que la clave del check (de la que cuelga el gate de
/// radicación) sea invariante al proveedor. Las multas pasaron de <c>fail</c> a <c>warn</c>:
/// tener comparendos ya no impide CREAR el trámite (AC5). La radicación al OT conserva su gate.
/// </summary>
public static class VerifikSimitResultMapper
{
    private const string Provider = "verifik_simit";

    /// <summary>
    /// Estado del SIMIT que cuenta como comparendo pendiente. Coincidencia EXACTA, deliberada:
    /// el SIMIT también devuelve "Pendiente Curso" (multa en curso pedagógico, con valorPagar en
    /// cero) y estados nulos. Ampliar esta coincidencia haría que más trámites se bloqueen en la
    /// RADICACIÓN, que sigue derivándose de este check.
    /// </summary>
    private const string EstadoPendiente = "Pendiente";

    /// <summary>
    /// Estado de CARTERA que cuenta como comparendo con deuda. En la respuesta viva de Verifik el
    /// comparendo escalado a resolución trae <c>estadoComparendo=null</c> pero
    /// <c>estadoCartera="Pendiente de pago"</c>: sin este criterio, una multa real no se detectaría.
    /// </summary>
    private const string CarteraPendiente = "Pendiente de pago";

    public static ConsultationResult Map(VerifikSimitResponse response)
    {
        // Acepta el envoltorio documentado (value.value.data) y el plano de la API viva (data al top).
        var data = response.Value?.Value?.Data ?? response.Data;

        var checks = new List<ConsultationCheck>
        {
            MapMultas(data),
            MapAcuerdosPago(data),
        };

        var overall = FinesCheckFactory.ComputeOverall(checks);
        return new ConsultationResult(Provider, overall, checks, []);
    }

    private static ConsultationCheck MapMultas(VerifikSimitData? data)
    {
        if (data is null)
            return FinesCheckFactory.SinDatos(Provider, FinesCheckFactory.KeyMultas, FinesCheckFactory.LabelMultas);

        var pendientes = (data.Multas ?? [])
            .Where(EsPendiente)
            .ToList();

        var detalle = pendientes.Select(ToFineDetail).ToList();
        return FinesCheckFactory.Multas(Provider, pendientes.Count, pendientes.Sum(m => m.ValorPagar ?? 0), detalle);
    }

    /// <summary>
    /// Un comparendo cuenta como pendiente si su <c>estadoComparendo</c> es "Pendiente" (forma
    /// documentada) O su <c>estadoCartera</c> es "Pendiente de pago" (forma viva, comparendo en cobro).
    /// "Pendiente Curso" (curso pedagógico, sin deuda) sigue sin contar.
    /// </summary>
    private static bool EsPendiente(VerifikSimitMulta m) =>
        string.Equals(m.EstadoComparendo, EstadoPendiente, StringComparison.OrdinalIgnoreCase)
        || string.Equals(m.EstadoCartera, CarteraPendiente, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Comparendo SIMIT → <see cref="FineDetail"/>. Solo datos del comparendo (número, fecha, valor,
    /// organismo, estado e infracción); nunca los del infractor (PII / Habeas Data).
    /// </summary>
    private static FineDetail ToFineDetail(VerifikSimitMulta m) => new(
        m.NumeroComparendo,
        LimpiarFecha(m.FechaComparendo),
        m.ValorPagar ?? m.Valor,
        m.OrganismoTransito,
        // estadoComparendo puede venir null en la respuesta viva; se cae a estadoCartera para el display.
        string.IsNullOrWhiteSpace(m.EstadoComparendo) ? m.EstadoCartera : m.EstadoComparendo,
        DescribeInfracciones(m.Infracciones));

    /// <summary>La fecha viva llega como "26/01/2025 00:00:00"; se deja solo la fecha (antes del espacio).</summary>
    private static string? LimpiarFecha(string? fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha))
            return null;
        var idx = fecha.IndexOf(' ');
        return idx > 0 ? fecha[..idx] : fecha;
    }

    /// <summary>Une las descripciones de las infracciones del comparendo en una sola línea legible.</summary>
    private static string? DescribeInfracciones(List<VerifikSimitInfraccion>? infracciones)
    {
        var descripciones = (infracciones ?? [])
            .Select(i => string.IsNullOrWhiteSpace(i.DescripcionInfraccion)
                ? i.CodigoInfraccion
                : i.DescripcionInfraccion)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList();

        return descripciones.Count == 0 ? null : string.Join("; ", descripciones);
    }

    private static ConsultationCheck MapAcuerdosPago(VerifikSimitData? data)
    {
        if (data is null)
            return FinesCheckFactory.SinDatos(Provider, FinesCheckFactory.KeyAcuerdos, FinesCheckFactory.LabelAcuerdos);

        var acuerdos = data.AcuerdosPago ?? [];
        return FinesCheckFactory.Acuerdos(Provider, acuerdos.Count, acuerdos.Sum(a => a.Pendiente ?? 0));
    }
}

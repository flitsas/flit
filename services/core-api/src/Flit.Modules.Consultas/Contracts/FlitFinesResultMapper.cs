namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// FEATURE 05 — mapper puro de la fuente INTERNA de comparendos → <see cref="ConsultationResult"/>.
/// Nunca lanza; robusto ante nulls.
///
/// Construye los checks con <see cref="FinesCheckFactory"/>, igual que
/// <see cref="VerifikSimitResultMapper"/>: las claves deben ser idénticas entre proveedores porque
/// de ellas cuelga el gate de radicación al OT. Un usuario con fuente interna y otro con fuente
/// externa deben producir exactamente el mismo contrato hacia el wizard.
/// </summary>
public static class FlitFinesResultMapper
{
    private const string Provider = "flit_fines";

    /// <summary>
    /// Estado que cuenta como comparendo pendiente. Coincidencia EXACTA, igual que en el mapper de
    /// Verifik: la fuente real también devuelve "Pendiente Curso" (multa en curso pedagógico, con
    /// valorPagar en cero) y estados nulos, que NO son deuda. Ampliar la coincidencia bloquearía
    /// más trámites en la RADICACIÓN, que se deriva de este check.
    /// </summary>
    private const string EstadoPendiente = "Pendiente";

    /// <summary>
    /// Estado de CARTERA que cuenta como deuda. En la respuesta viva el comparendo escalado a
    /// resolución trae <c>estadoComparendo=null</c> pero <c>estadoCartera="Pendiente de pago"</c>.
    /// </summary>
    private const string CarteraPendiente = "Pendiente de pago";

    public static ConsultationResult Map(FlitFinesResponse? response)
    {
        var checks = new List<ConsultationCheck>
        {
            MapMultas(response),
            MapAcuerdosPago(response),
        };

        return new ConsultationResult(Provider, FinesCheckFactory.ComputeOverall(checks), checks, []);
    }

    private static ConsultationCheck MapMultas(FlitFinesResponse? data)
    {
        if (data?.Multas is null)
            return FinesCheckFactory.SinDatos(Provider, FinesCheckFactory.KeyMultas, FinesCheckFactory.LabelMultas);

        // Conteo e importe SIEMPRE desde el detalle: los agregados de esta fuente mienten
        // (totalMultasPagar trae la cantidad, no el monto). Ver doc de FlitFinesResponse.
        var pendientes = data.Multas
            .Where(EsPendiente)
            .ToList();

        var detalle = pendientes.Select(ToFineDetail).ToList();
        return FinesCheckFactory.Multas(Provider, pendientes.Count, pendientes.Sum(m => m.ValorPagar ?? 0), detalle);
    }

    /// <summary>
    /// Pendiente si <c>estadoComparendo=="Pendiente"</c> (forma documentada) O
    /// <c>estadoCartera=="Pendiente de pago"</c> (forma viva, comparendo en cobro). "Pendiente Curso"
    /// (curso pedagógico, sin deuda) sigue sin contar.
    /// </summary>
    private static bool EsPendiente(FlitFinesMulta m) =>
        string.Equals(m.EstadoComparendo, EstadoPendiente, StringComparison.OrdinalIgnoreCase)
        || string.Equals(m.EstadoCartera, CarteraPendiente, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Comparendo → <see cref="FineDetail"/>. Solo datos del comparendo (número, fecha, valor,
    /// organismo, estado e infracción); nunca los del infractor (PII / Habeas Data).
    /// </summary>
    private static FineDetail ToFineDetail(FlitFinesMulta m) => new(
        m.NumeroComparendo,
        LimpiarFecha(m.FechaComparendo),
        m.ValorPagar,
        m.OrganismoTransito,
        string.IsNullOrWhiteSpace(m.EstadoComparendo) ? m.EstadoCartera : m.EstadoComparendo,
        DescribeInfracciones(m.Infracciones));

    /// <summary>La fecha llega como "26/01/2025 00:00:00"; se deja solo la fecha (antes del espacio).</summary>
    private static string? LimpiarFecha(string? fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha))
            return null;
        var idx = fecha.IndexOf(' ');
        return idx > 0 ? fecha[..idx] : fecha;
    }

    /// <summary>Une las descripciones de las infracciones del comparendo en una sola línea legible.</summary>
    private static string? DescribeInfracciones(List<FlitFinesInfraccion>? infracciones)
    {
        var descripciones = (infracciones ?? [])
            .Select(i => string.IsNullOrWhiteSpace(i.DescripcionInfraccion) ? i.CodigoInfraccion : i.DescripcionInfraccion)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList();

        return descripciones.Count == 0 ? null : string.Join("; ", descripciones);
    }

    private static ConsultationCheck MapAcuerdosPago(FlitFinesResponse? data)
    {
        if (data?.AcuerdosPago is null)
            return FinesCheckFactory.SinDatos(Provider, FinesCheckFactory.KeyAcuerdos, FinesCheckFactory.LabelAcuerdos);

        return FinesCheckFactory.Acuerdos(
            Provider, data.AcuerdosPago.Count, data.AcuerdosPago.Sum(a => a.Pendiente ?? 0));
    }
}

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
            .Where(m => string.Equals(m.EstadoComparendo, EstadoPendiente, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return FinesCheckFactory.Multas(Provider, pendientes.Count, pendientes.Sum(m => m.ValorPagar ?? 0));
    }

    private static ConsultationCheck MapAcuerdosPago(FlitFinesResponse? data)
    {
        if (data?.AcuerdosPago is null)
            return FinesCheckFactory.SinDatos(Provider, FinesCheckFactory.KeyAcuerdos, FinesCheckFactory.LabelAcuerdos);

        return FinesCheckFactory.Acuerdos(
            Provider, data.AcuerdosPago.Count, data.AcuerdosPago.Sum(a => a.Pendiente ?? 0));
    }
}

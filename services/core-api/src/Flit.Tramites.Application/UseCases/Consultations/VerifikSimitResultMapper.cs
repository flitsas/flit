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

    public static ConsultationResult Map(VerifikSimitResponse response)
    {
        var data = response.Value?.Value?.Data;

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
            .Where(m => string.Equals(m.EstadoComparendo, EstadoPendiente, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return FinesCheckFactory.Multas(Provider, pendientes.Count, pendientes.Sum(m => m.ValorPagar ?? 0));
    }

    private static ConsultationCheck MapAcuerdosPago(VerifikSimitData? data)
    {
        if (data is null)
            return FinesCheckFactory.SinDatos(Provider, FinesCheckFactory.KeyAcuerdos, FinesCheckFactory.LabelAcuerdos);

        var acuerdos = data.AcuerdosPago ?? [];
        return FinesCheckFactory.Acuerdos(Provider, acuerdos.Count, acuerdos.Sum(a => a.Pendiente ?? 0));
    }
}

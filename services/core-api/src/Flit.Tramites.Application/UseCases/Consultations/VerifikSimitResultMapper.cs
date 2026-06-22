namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Mapper puro Verifik SIMIT → <see cref="ConsultationResult"/> normalizado.
/// Aplica para respuestas por documento (§3.5) y por placa (§3.6): misma forma.
/// Semáforo: multas pendientes → fail; acuerdos de pago activos → warn; sin deudas → ok.
/// Nunca lanza; robusto ante nulls.
/// </summary>
public static class VerifikSimitResultMapper
{
    private const string Provider = "verifik_simit";

    private const string Ok = "ok";
    private const string Warn = "warn";
    private const string Fail = "fail";
    private const string Unknown = "unknown";

    private const string Green = "green";
    private const string Yellow = "yellow";
    private const string Red = "red";

    public static ConsultationResult Map(VerifikSimitResponse response)
    {
        var data = response.Value?.Value?.Data;

        var checks = new List<ConsultationCheck>
        {
            MapMultas(data),
            MapAcuerdosPago(data),
        };

        var overall = ComputeOverall(checks);
        return new ConsultationResult(Provider, overall, checks, []);
    }

    private static ConsultationCheck MapMultas(VerifikSimitData? data)
    {
        if (data is null)
            return new ConsultationCheck("multas", "Multas SIMIT", Unknown, Provider, "Sin datos SIMIT");

        var multas = data.Multas ?? [];
        var pendientes = multas
            .Where(m => string.Equals(m.EstadoComparendo, "Pendiente", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pendientes.Count == 0)
            return new ConsultationCheck("multas", "Multas SIMIT", Ok, Provider, null);

        var total = pendientes.Sum(m => m.ValorPagar ?? 0);
        return new ConsultationCheck(
            "multas",
            "Multas SIMIT",
            Fail,
            Provider,
            $"{pendientes.Count} multa(s) pendiente(s) por ${total:N0} COP");
    }

    private static ConsultationCheck MapAcuerdosPago(VerifikSimitData? data)
    {
        if (data is null)
            return new ConsultationCheck("acuerdos_pago", "Acuerdos de pago SIMIT", Unknown, Provider, "Sin datos SIMIT");

        var acuerdos = data.AcuerdosPago ?? [];
        if (acuerdos.Count == 0)
            return new ConsultationCheck("acuerdos_pago", "Acuerdos de pago SIMIT", Ok, Provider, null);

        var total = acuerdos.Sum(a => a.Pendiente ?? 0);
        return new ConsultationCheck(
            "acuerdos_pago",
            "Acuerdos de pago SIMIT",
            Warn,
            Provider,
            $"{acuerdos.Count} acuerdo(s) activo(s) por ${total:N0} COP");
    }

    private static string ComputeOverall(IReadOnlyList<ConsultationCheck> checks)
    {
        if (checks.Any(c => c.Status == Fail)) return Red;
        if (checks.Any(c => c.Status == Warn)) return Yellow;
        if (checks.Any(c => c.Status == Ok)) return Green;
        return Yellow;
    }
}

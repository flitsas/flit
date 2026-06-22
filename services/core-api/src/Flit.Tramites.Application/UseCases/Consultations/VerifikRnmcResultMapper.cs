namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Mapper puro Verifik RNMC (§3.1) → <see cref="ConsultationResult"/> normalizado.
/// Semáforo: correctiveMeasures[] vacío → ok (sin medidas); con medidas → fail.
/// Nunca lanza; robusto ante nulls.
/// </summary>
public static class VerifikRnmcResultMapper
{
    private const string Provider = "verifik_rnmc";

    private const string Ok = "ok";
    private const string Fail = "fail";
    private const string Unknown = "unknown";

    private const string Green = "green";
    private const string Yellow = "yellow";
    private const string Red = "red";

    public static ConsultationResult Map(VerifikRnmcResponse response)
    {
        var data = response.Data;

        var checks = new List<ConsultationCheck>
        {
            MapCorrectiveMeasures(data),
        };

        var hydrated = MapHydratedFields(data);
        var overall = ComputeOverall(checks);

        return new ConsultationResult(Provider, overall, checks, hydrated);
    }

    private static ConsultationCheck MapCorrectiveMeasures(VerifikRnmcData? data)
    {
        if (data is null)
            return new ConsultationCheck(
                "medidas_correctivas",
                "Medidas correctivas (Policía)",
                Unknown,
                Provider,
                "Sin datos RNMC");

        var measures = data.CorrectiveMeasures ?? [];

        if (measures.Count == 0)
            return new ConsultationCheck(
                "medidas_correctivas",
                "Medidas correctivas (Policía)",
                Ok,
                Provider,
                null);

        var resumen = measures
            .Select(m => m.Measure)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .Take(3)
            .ToList();

        var desc = resumen.Count > 0
            ? $"{measures.Count} medida(s): {string.Join(", ", resumen)}"
            : $"{measures.Count} medida(s) correctiva(s) registrada(s)";

        return new ConsultationCheck(
            "medidas_correctivas",
            "Medidas correctivas (Policía)",
            Fail,
            Provider,
            desc);
    }

    private static List<HydratedField> MapHydratedFields(VerifikRnmcData? data)
    {
        if (data is null) return [];

        var fields = new List<HydratedField>();

        if (!string.IsNullOrWhiteSpace(data.FullName))
            fields.Add(new HydratedField("owner_full_name", data.FullName, null));

        return fields;
    }

    private static string ComputeOverall(IReadOnlyList<ConsultationCheck> checks)
    {
        if (checks.Any(c => c.Status == Fail)) return Red;
        if (checks.Any(c => c.Status == Ok)) return Green;
        return Yellow;
    }
}

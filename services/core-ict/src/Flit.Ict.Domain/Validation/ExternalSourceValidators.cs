namespace Flit.Ict.Domain.Validation;

/// <summary>Datos normalizados de las consultas a fuentes externas de un pre-trámite.</summary>
public sealed record ConsultationResult(
    string? SoatStatus = null,
    string? RtmStatus = null,
    int? VehicleModelYear = null,
    bool HasActiveSanctions = false,
    bool? PazYSalvo = null);

/// <summary>
/// Validadores de negocio externo portados de v1 (SOAT, RTM/antigüedad, RNMC, paz y salvo).
/// Lógica pura (sin I/O) — el orquestador les pasa el resultado normalizado y ellos deciden si
/// el pre-trámite queda con novedades.
/// </summary>
public static class ExternalSourceValidators
{
    public static IReadOnlyList<string> Validate(int transactionType, ConsultationResult result, int currentYear)
    {
        ArgumentNullException.ThrowIfNull(result);
        var issues = new List<string>();

        // SOAT: obligatorio y vigente.
        if (result.SoatStatus is not null
            && !string.Equals(result.SoatStatus, "VIGENTE", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("SOAT no vigente");
        }

        // RTM: requerida según antigüedad (>5 años). Traspaso unilateral (4) solo advierte (no bloquea).
        if (result.VehicleModelYear is { } modelYear)
        {
            var age = currentYear - modelYear;
            var rtmRequired = age > 5;
            var rtmVigente = string.Equals(result.RtmStatus, "VIGENTE", StringComparison.OrdinalIgnoreCase);
            if (rtmRequired && !rtmVigente && transactionType != 4)
            {
                issues.Add("Revisión técnico-mecánica (RTM) no vigente");
            }
        }

        // RNMC: bloquea si hay sanciones/medidas correctivas activas.
        if (result.HasActiveSanctions)
        {
            issues.Add("El actor tiene sanciones o medidas correctivas activas (RNMC)");
        }

        // DRIVER: paz y salvo del propietario.
        if (result.PazYSalvo == false)
        {
            issues.Add("El propietario no está a paz y salvo");
        }

        return issues;
    }
}

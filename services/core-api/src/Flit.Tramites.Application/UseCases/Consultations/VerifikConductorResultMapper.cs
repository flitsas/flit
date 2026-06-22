namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Mapper puro Verifik CONDUCTOR (RUNT) → <see cref="ConsultationResult"/> normalizado.
/// Hidrata el nombre de la persona (person_full_name / person_first_name /
/// person_last_name) y, si está, person_license_status. Check "conductor":
/// ok cuando se halla la persona; unknown cuando no hay nombre. Nunca lanza.
/// </summary>
public static class VerifikConductorResultMapper
{
    private const string Provider = "verifik_conductor";

    private const string Ok = "ok";
    private const string Unknown = "unknown";

    private const string Green = "green";
    private const string Yellow = "yellow";

    public static ConsultationResult Map(VerifikConductorResponse response)
    {
        var data = response.Data;

        var fullName = data?.FullName?.Trim();
        var found = !string.IsNullOrWhiteSpace(fullName);

        var check = found
            ? new ConsultationCheck("conductor", "Persona en RUNT", Ok, Provider, null)
            : new ConsultationCheck("conductor", "Persona en RUNT", Unknown, Provider, "Persona no encontrada en RUNT");

        var hydrated = MapHydratedFields(data, found);
        var overall = found ? Green : Yellow;

        return new ConsultationResult(Provider, overall, [check], hydrated);
    }

    private static List<HydratedField> MapHydratedFields(VerifikConductorData? data, bool found)
    {
        if (data is null || !found) return [];

        var fields = new List<HydratedField>();

        if (!string.IsNullOrWhiteSpace(data.FullName))
            fields.Add(new HydratedField("person_full_name", data.FullName.Trim(), null));

        if (!string.IsNullOrWhiteSpace(data.FirstName))
            fields.Add(new HydratedField("person_first_name", data.FirstName.Trim(), null));

        if (!string.IsNullOrWhiteSpace(data.LastName))
            fields.Add(new HydratedField("person_last_name", data.LastName.Trim(), null));

        var licenseStatus = data.IdentityValidationAttempts?.EstadoUsuario;
        if (!string.IsNullOrWhiteSpace(licenseStatus))
            fields.Add(new HydratedField("person_license_status", licenseStatus.Trim(), null));

        return fields;
    }
}

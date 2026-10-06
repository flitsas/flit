namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Mapper puro Verifik CONDUCTOR (RUNT) → <see cref="ConsultationResult"/> normalizado.
/// Produce 4 checks cuando la persona es encontrada: conductor_identidad, conductor_estado,
/// conductor_licencia y conductor_multas (este último solo si Infractions no es null).
/// Overall: green si found + sin multas + licencia activa; yellow en cualquier otro caso.
/// </summary>
public static class VerifikConductorResultMapper
{
    private const string Provider = "verifik_conductor";

    private const string Ok = "ok";
    private const string Unknown = "unknown";
    private const string Warn = "warn";
    private const string Fail = "fail";

    private const string Green = "green";
    private const string Yellow = "yellow";

    public static ConsultationResult Map(VerifikConductorResponse response)
    {
        var data = response.Data;

        var names = ResolveNames(data);
        var fullName = DriverNameResolver.ResolveFullName(data?.FullName, names);
        var found = names.HasAny || fullName.Length > 0;

        var checks = new List<ConsultationCheck>();

        // Check 1: identidad — ok si se halló la persona, unknown si no
        checks.Add(found
            ? new ConsultationCheck("conductor_identidad", "Persona en RUNT", Ok, Provider, null)
            : new ConsultationCheck("conductor_identidad", "Persona en RUNT", Unknown, Provider, "Persona no encontrada en RUNT"));

        if (found && data is not null)
        {
            // Check 2: estado — ok solo si driverStatus==ACTIVO Y citizenStatus==ACTIVA
            var driverOk = string.Equals(data.DriverStatus, "ACTIVO", StringComparison.OrdinalIgnoreCase);
            var citizenOk = string.Equals(data.CitizenStatus, "ACTIVA", StringComparison.OrdinalIgnoreCase);
            checks.Add(new ConsultationCheck("conductor_estado", "Estado conductor",
                driverOk && citizenOk ? Ok : Warn, Provider, null));

            // Check 3: licencia — ok si hay al menos 1 licencia ACTIVA
            var hasActiveLicense = data.Licenses?.Any(l =>
                string.Equals(l.Status, "ACTIVA", StringComparison.OrdinalIgnoreCase)) == true;
            checks.Add(new ConsultationCheck("conductor_licencia", "Licencia conductor",
                hasActiveLicense ? Ok : Warn, Provider, null));

            // Check 4: multas — solo si hay datos de infracciones
            if (data.Infractions is not null)
            {
                var tieneMultas = string.Equals(data.Infractions.TieneMultas, "SI", StringComparison.OrdinalIgnoreCase);
                checks.Add(new ConsultationCheck("conductor_multas", "Multas conductor",
                    tieneMultas ? Fail : Ok, Provider, null));
            }
        }

        var hydrated = MapHydratedFields(data, names, fullName, found);

        var hasActiveLicenseForOverall = found && data is not null &&
            data.Licenses?.Any(l => string.Equals(l.Status, "ACTIVA", StringComparison.OrdinalIgnoreCase)) == true;
        var hasPendingFines = found && data?.Infractions is not null &&
            string.Equals(data.Infractions.TieneMultas, "SI", StringComparison.OrdinalIgnoreCase);

        var overall = (found && !hasPendingFines && hasActiveLicenseForOverall) ? Green : Yellow;

        return new ConsultationResult(Provider, overall, checks, hydrated);
    }

    /// <summary>
    /// Nombre real de la persona. Verifik entrega <c>firstName</c> con TODOS los nombres de pila y
    /// <c>lastName</c> con todos los apellidos; <c>primerApellido</c>/<c>segundoApellido</c> solo
    /// llegan en algunas respuestas y, cuando están, evitan tener que separar por heurística. Si no
    /// vino ninguno de esos campos se cae a separar el <c>fullName</c>.
    /// </summary>
    private static DriverNames ResolveNames(VerifikConductorData? data)
    {
        if (data is null)
            return DriverNames.Empty;

        var fromFields = DriverNameResolver.FromCombined(
            data.FirstName, data.LastName, data.PrimerApellido, data.SegundoApellido);

        return fromFields.HasAny ? fromFields : DriverNameResolver.FromFullName(data.FullName);
    }

    private static List<HydratedField> MapHydratedFields(
        VerifikConductorData? data, DriverNames names, string fullName, bool found)
    {
        if (data is null || !found) return [];

        var fields = new List<HydratedField>();

        DriverNameResolver.AddHydratedNames(fields, names, fullName);

        // Usar driverStatus (no identityValidationAttempts.estadoUsuario) para person_license_status
        if (!string.IsNullOrWhiteSpace(data.DriverStatus))
            fields.Add(new HydratedField("person_license_status", data.DriverStatus.Trim(), null));

        if (!string.IsNullOrWhiteSpace(data.CitizenStatus))
            fields.Add(new HydratedField("person_citizen_status", data.CitizenStatus.Trim(), null));

        if (data.Infractions is not null)
        {
            var tieneMultas = string.Equals(data.Infractions.TieneMultas, "SI", StringComparison.OrdinalIgnoreCase);
            fields.Add(new HydratedField("person_has_pending_fines", tieneMultas ? "true" : "false", null));

            if (!string.IsNullOrWhiteSpace(data.Infractions.NroPazYSalvo))
                fields.Add(new HydratedField("person_paz_y_salvo", data.Infractions.NroPazYSalvo.Trim(), null));
        }

        var activeLicenses = data.Licenses?
            .Where(l => string.Equals(l.Status, "ACTIVA", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        fields.Add(new HydratedField("person_has_active_license", activeLicenses.Count > 0 ? "true" : "false", null));

        // Distinct como en Kyverum: Verifik trae una fila por categoría, así que dos licencias
        // activas de la misma categoría repetían el valor ("B1,B1") y divergían del otro proveedor.
        var categories = activeLicenses
            .Where(l => !string.IsNullOrWhiteSpace(l.Category))
            .Select(l => l.Category!.Trim())
            .Distinct()
            .ToList();

        if (categories.Count > 0)
            fields.Add(new HydratedField("person_license_categories", string.Join(",", categories), null));

        return fields;
    }
}

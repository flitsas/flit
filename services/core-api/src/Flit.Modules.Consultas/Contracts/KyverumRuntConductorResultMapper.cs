namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Mapper puro Kyverum RUNT persona/conductor → <see cref="ConsultationResult"/> normalizado
/// (HU #10478). Kyverum-first: mismos <c>Check.Key</c> (conductor_identidad, conductor_estado,
/// conductor_licencia, conductor_multas) e <c>HydratedField.FieldKey</c> que
/// <see cref="VerifikConductorResultMapper"/>; solo cambia <c>Source</c> = <c>kyverum_runt_conductor</c>.
/// Overall: green si persona hallada + sin multas + licencia activa; yellow en cualquier otro caso.
/// Nunca lanza.
/// </summary>
public static class KyverumRuntConductorResultMapper
{
    private const string Provider = "kyverum_runt_conductor";

    private const string Ok = "ok";
    private const string Unknown = "unknown";
    private const string Warn = "warn";
    private const string Fail = "fail";

    private const string Green = "green";
    private const string Yellow = "yellow";

    public static ConsultationResult Map(KyverumRuntPersonaResponse response)
    {
        var persona = response.Persona;
        var names = ResolveNames(response);
        var fullName = DriverNameResolver.ResolveFullName(
            response.Identidad?.NombreCompleto ?? persona?.NombreCompleto, names);
        var found = names.HasAny || fullName.Length > 0;

        var checks = new List<ConsultationCheck>();

        // Check 1: identidad — ok si se halló la persona, unknown si no.
        checks.Add(found
            ? new ConsultationCheck("conductor_identidad", "Persona en RUNT", Ok, Provider, null)
            : new ConsultationCheck("conductor_identidad", "Persona en RUNT", Unknown, Provider, "Persona no encontrada en RUNT"));

        if (found && persona is not null)
        {
            // Check 2: estado — ok solo si estadoConductor==ACTIVO Y estadoPersona==ACTIVA.
            var driverOk = string.Equals(persona.EstadoConductor, "ACTIVO", StringComparison.OrdinalIgnoreCase);
            var citizenOk = string.Equals(persona.EstadoPersona, "ACTIVA", StringComparison.OrdinalIgnoreCase);
            checks.Add(new ConsultationCheck("conductor_estado", "Estado conductor",
                driverOk && citizenOk ? Ok : Warn, Provider, null));

            // Check 3: licencia — ok si hay al menos 1 licencia ACTIVA.
            checks.Add(new ConsultationCheck("conductor_licencia", "Licencia conductor",
                HasActiveLicense(response) ? Ok : Warn, Provider, null));

            // Check 4: multas — solo si hay bloque de multas.
            if (response.Multas is not null)
            {
                var tieneMultas = string.Equals(response.Multas.TieneMultas, "SI", StringComparison.OrdinalIgnoreCase);
                checks.Add(new ConsultationCheck("conductor_multas", "Multas conductor",
                    tieneMultas ? Fail : Ok, Provider, null));
            }
        }

        var hydrated = MapHydratedFields(response, names, fullName, found);

        var hasActiveLicense = found && HasActiveLicense(response);
        var hasPendingFines = found && response.Multas is not null &&
            string.Equals(response.Multas.TieneMultas, "SI", StringComparison.OrdinalIgnoreCase);

        var overall = (found && !hasPendingFines && hasActiveLicense) ? Green : Yellow;

        return new ConsultationResult(Provider, overall, checks, hydrated);
    }

    private static bool HasActiveLicense(KyverumRuntPersonaResponse response) =>
        response.Licencias?.Any(l => string.Equals(l?.EstadoLicencia, "ACTIVA", StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// Nombre real de la persona. <c>persona.nombres</c>/<c>apellidos</c> llegan ENMASCARADOS desde la
    /// actualización del RUNT y por eso no se leen nunca: la fuente es <c>identidad</c> y, si no vino,
    /// los desglosados de <c>persona</c>. El <c>nombreCompleto</c> queda como último recurso porque
    /// obliga a separar por heurística lo que el proveedor ya trae separado.
    /// </summary>
    private static DriverNames ResolveNames(KyverumRuntPersonaResponse response)
    {
        var identidad = response.Identidad;
        var fromIdentidad = DriverNameResolver.FromParts(
            identidad?.PrimerNombre, identidad?.SegundoNombre,
            identidad?.PrimerApellido, identidad?.SegundoApellido);
        if (fromIdentidad.HasAny)
            return fromIdentidad;

        var persona = response.Persona;
        var fromPersona = DriverNameResolver.FromParts(
            persona?.PrimerNombre, persona?.SegundoNombre,
            persona?.PrimerApellido, persona?.SegundoApellido);
        if (fromPersona.HasAny)
            return fromPersona;

        return DriverNameResolver.FromFullName(identidad?.NombreCompleto ?? persona?.NombreCompleto);
    }

    private static List<HydratedField> MapHydratedFields(
        KyverumRuntPersonaResponse response, DriverNames names, string fullName, bool found)
    {
        if (!found)
            return [];

        var fields = new List<HydratedField>();

        DriverNameResolver.AddHydratedNames(fields, names, fullName);
        Add(fields, "person_license_status", response.Persona?.EstadoConductor?.Trim());
        Add(fields, "person_citizen_status", response.Persona?.EstadoPersona?.Trim());

        if (response.Multas is not null)
        {
            var tieneMultas = string.Equals(response.Multas.TieneMultas, "SI", StringComparison.OrdinalIgnoreCase);
            fields.Add(new HydratedField("person_has_pending_fines", tieneMultas ? "true" : "false", null));
            Add(fields, "person_paz_y_salvo", response.Multas.NroPazYSalvo?.Trim());
        }

        var activeLicenses = response.Licencias?
            .Where(l => string.Equals(l?.EstadoLicencia, "ACTIVA", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        fields.Add(new HydratedField("person_has_active_license", activeLicenses.Count > 0 ? "true" : "false", null));

        var categories = activeLicenses
            .SelectMany(l => l.DetalleLicencia ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d?.Categoria))
            .Select(d => d.Categoria!.Trim())
            .Distinct()
            .ToList();

        if (categories.Count > 0)
            fields.Add(new HydratedField("person_license_categories", string.Join(",", categories), null));

        return fields;
    }

    private static void Add(List<HydratedField> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new HydratedField(key, value, null));
    }
}

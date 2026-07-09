using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;

/// <summary>
/// Valida el override de proveedores de consulta del request (HU #10478) y lo proyecta al modelo de
/// dominio <see cref="ConsultationProviderConfig"/>. Reglas: la clave debe ser un tipo de consulta
/// conocido (<c>vehicle_vin</c>, <c>vehicle_plate</c>, <c>conductor</c>); <c>primary</c> es obligatorio
/// y, junto con cada <c>fallback</c>, debe ser un proveedor permitido para ese tipo. Cualquier error se
/// acumula en <paramref name="errors"/> bajo el campo <c>consultationProviderConfig</c>.
/// </summary>
internal static class ConsultationConfigValidator
{
    private const string Field = "consultationProviderConfig";

    private static readonly Dictionary<string, string[]> AllowedByKind = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vehicle_vin"] = ["kyverum_runt", "verifik"],
        ["vehicle_plate"] = ["kyverum_runt", "verifik"],
        ["conductor"] = ["kyverum_runt_conductor", "verifik_conductor"],
    };

    /// <summary>
    /// Devuelve el config de dominio validado, o <c>null</c> cuando el request no trae override (se
    /// conserva el valor previo). Si hay errores, se agregan a <paramref name="errors"/> y el retorno
    /// no debe usarse (el handler corta antes de persistir).
    /// </summary>
    public static ConsultationProviderConfig? TryBuild(
        IReadOnlyDictionary<string, ConsultationProviderChoice>? request,
        List<SettingsValidationError> errors)
    {
        if (request is null)
        {
            return null;
        }

        var byKind = new Dictionary<string, ConsultationProviderSelection>(StringComparer.OrdinalIgnoreCase);

        foreach (var (kind, choice) in request)
        {
            if (!AllowedByKind.TryGetValue(kind, out var allowed))
            {
                errors.Add(new SettingsValidationError(
                    Field, $"Tipo de consulta desconocido: '{kind}'. Permitidos: vehicle_vin, vehicle_plate, conductor."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(choice?.Primary))
            {
                errors.Add(new SettingsValidationError(Field, $"'{kind}': primary es obligatorio."));
                continue;
            }

            if (!IsAllowed(choice.Primary, allowed))
            {
                errors.Add(new SettingsValidationError(
                    Field, $"'{kind}': proveedor '{choice.Primary}' no permitido. Permitidos: {string.Join(", ", allowed)}."));
            }

            var fallback = choice.Fallback ?? [];
            foreach (var provider in fallback)
            {
                if (string.IsNullOrWhiteSpace(provider) || !IsAllowed(provider, allowed))
                {
                    errors.Add(new SettingsValidationError(
                        Field, $"'{kind}': fallback '{provider}' no permitido. Permitidos: {string.Join(", ", allowed)}."));
                }
            }

            byKind[kind] = new ConsultationProviderSelection(choice.Primary, [.. fallback]);
        }

        return new ConsultationProviderConfig(byKind);
    }

    private static bool IsAllowed(string provider, string[] allowed) =>
        allowed.Contains(provider, StringComparer.OrdinalIgnoreCase);
}

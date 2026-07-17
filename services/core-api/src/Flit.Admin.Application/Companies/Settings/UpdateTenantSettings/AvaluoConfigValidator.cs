using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;

/// <summary>
/// Valida la configuración de proveedores de avalúo del request (Feature #10707) y la proyecta al
/// modelo de dominio <see cref="AvaluoProviderConfig"/>. Reglas: cada proveedor habilitado debe ser
/// una key conocida (<c>fasecolda</c>, <c>base_gravable</c>, <c>mercado_libre</c>); Fasecolda siempre
/// queda habilitado (proveedor base); el <c>primary</c> debe ser uno de los habilitados. Cualquier
/// error se acumula en <paramref name="errors"/> bajo el campo <c>avaluoProviderConfig</c>.
/// </summary>
internal static class AvaluoConfigValidator
{
    private const string Field = "avaluoProviderConfig";

    private static readonly string[] KnownProviders =
        ["fasecolda", "base_gravable", "mercado_libre"];

    /// <summary>
    /// Devuelve el config de dominio validado, o <c>null</c> cuando el request no trae la sección (se
    /// conserva el valor previo). Si hay errores, se agregan a <paramref name="errors"/> y el retorno
    /// no debe usarse (el handler corta antes de persistir).
    /// </summary>
    public static AvaluoProviderConfig? TryBuild(
        AvaluoProviderConfigDto? request,
        List<SettingsValidationError> errors)
    {
        if (request is null)
        {
            return null;
        }

        var enabled = request.Enabled ?? [];
        foreach (var provider in enabled)
        {
            if (string.IsNullOrWhiteSpace(provider) || !IsKnown(provider))
            {
                errors.Add(new SettingsValidationError(
                    Field, $"Proveedor de avalúo desconocido: '{provider}'. Permitidos: {string.Join(", ", KnownProviders)}."));
            }
        }

        var primary = string.IsNullOrWhiteSpace(request.Primary)
            ? AvaluoProviderConfig.BaseProvider
            : request.Primary;
        if (!IsKnown(primary))
        {
            errors.Add(new SettingsValidationError(
                Field, $"Proveedor sugerido desconocido: '{primary}'. Permitidos: {string.Join(", ", KnownProviders)}."));
        }

        if (errors.Count > 0)
        {
            return null;
        }

        // El constructor de dominio fuerza a Fasecolda habilitado y normaliza el primario.
        return new AvaluoProviderConfig(primary, [.. enabled]);
    }

    private static bool IsKnown(string provider) =>
        KnownProviders.Contains(provider, StringComparer.OrdinalIgnoreCase);
}

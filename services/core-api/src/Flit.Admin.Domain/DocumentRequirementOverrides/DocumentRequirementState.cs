namespace Flit.Admin.Domain.DocumentRequirementOverrides;

/// <summary>
/// Estados de la obligatoriedad documental por Organismo de Tránsito (HU #10198). Tres
/// estados persistibles más <see cref="Default"/>, que no se persiste: significa "limpiar
/// el override" (la fila se borra y el documento vuelve a heredar el default del trámite).
/// Los valores persistibles se guardan <b>exactamente</b> en
/// <c>document_requirement_overrides.requirement_state</c>.
/// </summary>
public static class DocumentRequirementState
{
    /// <summary>Obligatorio para este OT.</summary>
    public const string Required = "REQUIRED";

    /// <summary>Opcional para este OT (visible pero no requisito).</summary>
    public const string Optional = "OPTIONAL";

    /// <summary>No aplica: el documento se oculta de la matriz de este OT.</summary>
    public const string NotApplicable = "NOT_APPLICABLE";

    /// <summary>Hereda el default del trámite (no se persiste; limpia el override).</summary>
    public const string Default = "DEFAULT";

    /// <summary>True si <paramref name="state"/> es un estado persistible válido.</summary>
    public static bool IsPersistable(string? state) =>
        state is Required or Optional or NotApplicable;

    /// <summary>
    /// Normaliza el valor recibido (trim + mayúsculas). Devuelve el estado si es
    /// <see cref="Default"/> o persistible; en caso contrario <c>null</c> (el endpoint
    /// responde 422).
    /// </summary>
    public static string? Normalize(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        var normalized = state.Trim().ToUpperInvariant();
        return normalized == Default || IsPersistable(normalized) ? normalized : null;
    }
}

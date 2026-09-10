namespace Flit.Modules.Security.Domain.UiPreferences;

/// <summary>
/// Preferencia de UI de un usuario para un <c>scope</c> puntual (p. ej. las columnas visibles de
/// una tabla), persistida por tenant + usuario + scope. <c>ValueJson</c> viaja como texto JSON
/// crudo (el mismo que se guarda en <c>admin.user_ui_preferences.value</c>, tipo jsonb) porque el
/// dominio no necesita interpretar su contenido: es opaco para el backend, la UI decide su forma.
/// </summary>
public sealed class UserUiPreference
{
    public Guid TenantId { get; init; }

    public Guid UserId { get; init; }

    public string Scope { get; init; } = string.Empty;

    public string ValueJson { get; init; } = "{}";
}

namespace Flit.Modules.Security.Domain.UiPreferences;

/// <summary>
/// Repositorio de preferencias de UI por usuario (<c>admin.user_ui_preferences</c>). Es la base
/// compartida de los criterios que permiten al usuario elegir qué columnas ve en las tablas de
/// trámites: un único par lectura/escritura por tenant + usuario + scope.
/// </summary>
public interface IUserUiPreferenceRepository
{
    /// <summary>
    /// Busca la preferencia guardada. <c>null</c> si el usuario nunca la guardó — el caller
    /// (handler de Application) es quien decide devolver <c>{}</c> en ese caso, NO este método:
    /// así queda explícito en el nombre del método qué significa "no hay fila".
    /// </summary>
    Task<UserUiPreference?> FindAsync(
        Guid tenantId,
        Guid userId,
        string scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert idempotente: crea la fila si no existe, o sobrescribe <c>value</c> si ya existía
    /// (nunca duplica). Concurrencia optimista fuera de alcance a propósito — la última escritura
    /// gana, igual que cualquier preferencia de UI (no hay "conflicto" de negocio real entre dos
    /// pestañas del mismo usuario cambiando el orden de columnas).
    /// </summary>
    Task<UserUiPreference> UpsertAsync(
        Guid tenantId,
        Guid userId,
        string scope,
        string valueJson,
        CancellationToken cancellationToken = default);
}

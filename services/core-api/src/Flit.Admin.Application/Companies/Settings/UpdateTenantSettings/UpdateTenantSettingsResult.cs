namespace Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;

/// <summary>
/// Resultado del comando de actualización. <see cref="IsValid"/> falso indica
/// fallo de validación (AC2 → HTTP 422) sin persistir nada; verdadero indica
/// guardado atómico exitoso con la configuración resultante (AC1 → HTTP 200).
/// </summary>
public sealed class UpdateTenantSettingsResult
{
    private UpdateTenantSettingsResult(
        bool isValid,
        TenantSettingsResponse? settings,
        IReadOnlyList<SettingsValidationError> errors)
    {
        IsValid = isValid;
        Settings = settings;
        Errors = errors;
    }

    public bool IsValid { get; }

    public TenantSettingsResponse? Settings { get; }

    public IReadOnlyList<SettingsValidationError> Errors { get; }

    public static UpdateTenantSettingsResult Success(TenantSettingsResponse settings) =>
        new(true, settings, []);

    public static UpdateTenantSettingsResult Invalid(IReadOnlyList<SettingsValidationError> errors) =>
        new(false, null, errors);
}

namespace Flit.Admin.Application.Companies.Settings;

/// <summary>
/// Error de validación de configuración (AC2). Serializado como
/// <c>{ field, message }</c> dentro de <c>{ errors: [...] }</c> con HTTP 422.
/// </summary>
/// <param name="Field">Campo del payload que falló la validación.</param>
/// <param name="Message">Mensaje legible del error.</param>
public sealed record SettingsValidationError(string Field, string Message);

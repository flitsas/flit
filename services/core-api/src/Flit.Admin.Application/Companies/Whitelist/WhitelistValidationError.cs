namespace Flit.Admin.Application.Companies.Whitelist;

/// <summary>
/// Error de validación de la lista blanca (AC5). Serializado como
/// <c>{ field, message, value }</c> dentro de <c>{ errors: [...] }</c> con HTTP 422.
/// </summary>
/// <param name="Field">Campo del payload que falló (siempre <c>emails</c>).</param>
/// <param name="Message">Mensaje legible del error.</param>
/// <param name="Value">Valor que causó el error (el correo inválido), si aplica.</param>
public sealed record WhitelistValidationError(string Field, string Message, string? Value);

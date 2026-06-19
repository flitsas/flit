namespace Flit.Admin.Application.Companies.CreateCompany;

/// <summary>
/// Error de validación del alta de compañía. Serializado como <c>{ field, message }</c>
/// dentro de <c>{ errors: [...] }</c> con HTTP 422.
/// </summary>
/// <param name="Field">Campo del payload que falló (camelCase, alineado con el cliente).</param>
/// <param name="Message">Mensaje legible del error.</param>
public sealed record CompanyValidationError(string Field, string Message);

namespace Flit.Admin.Application.Companies.Deeds;

/// <summary>
/// Error de validación de la gestión de escrituras (422). <c>Code</c> es un código estable para el FE
/// (p. ej. <c>requerido</c>, <c>vigencia_invalida</c>, <c>companias_requeridas</c>,
/// <c>documento_requerido</c>). El sobre de respuesta es <c>{ errors: [{ field, code, message }] }</c>.
/// </summary>
public sealed record DeedValidationError(string Field, string Code, string Message);

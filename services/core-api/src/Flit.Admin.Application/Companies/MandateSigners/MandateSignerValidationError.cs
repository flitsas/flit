namespace Flit.Admin.Application.Companies.MandateSigners;

/// <summary>
/// Error de validación de un mandatario (422). <c>Value</c> nunca debe contener el número de
/// documento (PII, Ley 1581): solo ids de compañía/OT o el nombre del mandatario en conflicto.
/// </summary>
public sealed record MandateSignerValidationError(string Field, string Message, string? Value);

namespace Flit.Admin.Application.Companies.SignatureVault;

/// <summary>
/// Error de validación del baúl de firmas (422). <c>Code</c> es un código estable para el FE
/// (p. ej. <c>firma_activa_existente</c>, <c>requerido</c>, <c>vigencia_invalida</c>). Ni el
/// <c>Message</c> ni ningún campo deben contener el número de documento (PII, Ley 1581).
/// </summary>
public sealed record SignatureVaultValidationError(string Field, string Code, string Message);

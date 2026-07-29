namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Error de validación del CRUD de representantes legales (422, HU #10901). <c>Code</c> es un código
/// estable para el FE (p. ej. <c>requerido</c>, <c>tipo_tramite_inexistente</c>). Ni el
/// <c>Message</c> ni ningún campo deben contener el número de documento (PII, Ley 1581).
/// </summary>
public sealed record LegalRepresentativeValidationError(string Field, string Code, string Message);

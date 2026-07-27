namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Datos normalizados de alta/edición de un representante legal, compartidos por los handlers Create y
/// Update (HU #10901). <c>Id</c> nulo = alta. Incluye los datos de la compañía representada (para el
/// upsert por NIT) y del representante. <c>DocumentNumber</c> y <c>CompanyNit</c> son PII (Ley 1581):
/// no loguear.
/// </summary>
public sealed record LegalRepresentativeWriteInput(
    Guid TenantId,
    Guid? Id,
    string? CompanyNit,
    string? CompanyName,
    string? CompanyEmail,
    string? CompanyAddress,
    string? CompanyCity,
    string? CompanyPhone,
    string? DocumentType,
    string? DocumentNumber,
    string? FirstLastName,
    string? SecondLastName,
    string? Name,
    string? Email,
    string? Address,
    string? City,
    string? Phone,
    IReadOnlyList<Guid> ProcedureTypeIds,
    Guid? ActorBy,
    IReadOnlyList<LegalRepresentativeCompanyInput>? Companies = null);

/// <summary>
/// Una compañía anidada del representante (HU #10932): la vista representante-céntrica envía la lista
/// de empresas del representante. Si el request no trae <c>Companies</c>, se usa la compañía única de
/// los campos <c>Company*</c> (compatibilidad). <c>Nit</c> es PII (Ley 1581): no loguear.
/// </summary>
public sealed record LegalRepresentativeCompanyInput(
    string? Nit,
    string? Name,
    string? Email,
    string? Address,
    string? City,
    string? Phone);

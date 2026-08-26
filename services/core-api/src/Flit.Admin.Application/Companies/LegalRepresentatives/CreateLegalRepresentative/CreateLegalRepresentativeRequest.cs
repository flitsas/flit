namespace Flit.Admin.Application.Companies.LegalRepresentatives.CreateLegalRepresentative;

/// <summary>
/// Cuerpo del alta de un representante legal (POST, HU #10901). Lleva los datos de la compañía
/// representada (se upserta por NIT) y del representante, más los tipos de trámite que puede firmar.
/// <c>DocumentNumber</c> y <c>CompanyNit</c> son PII (Ley 1581). <c>SignatureVaultId</c> es la firma
/// elegida explícitamente del baúl (HU #11175, AC3); si no viene, el sistema la resuelve.
/// </summary>
public sealed record CreateLegalRepresentativeRequest(
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
    IReadOnlyList<Guid>? ProcedureTypeIds,
    IReadOnlyList<LegalRepresentativeCompanyInput>? Companies = null,
    Guid? SignatureVaultId = null);

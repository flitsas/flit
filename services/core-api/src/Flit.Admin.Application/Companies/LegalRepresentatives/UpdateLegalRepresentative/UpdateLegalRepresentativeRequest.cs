namespace Flit.Admin.Application.Companies.LegalRepresentatives.UpdateLegalRepresentative;

/// <summary>
/// Cuerpo de la edición de un representante legal (PUT, HU #10901). El id viaja en la ruta. Mismo
/// contrato que el alta: re-upserta la compañía por NIT y re-resuelve firma/identidad al guardar.
/// <c>DocumentNumber</c> y <c>CompanyNit</c> son PII (Ley 1581). <c>SignatureVaultId</c> es la firma
/// elegida explícitamente del baúl (HU #11175, AC3); si no viene, el sistema la resuelve.
/// </summary>
public sealed record UpdateLegalRepresentativeRequest(
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

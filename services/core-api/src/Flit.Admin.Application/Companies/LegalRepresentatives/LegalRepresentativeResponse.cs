using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Vista de un representante legal para la gestión admin (HU #10901). Proyecta los datos denormalizados
/// de la compañía representada, las referencias de firma/identidad vigentes y los tipos de trámite del
/// puente M:N. <c>DocumentNumber</c> (y el NIT de la compañía) son PII (Ley 1581): se entregan solo en
/// respuestas autenticadas de gestión (SuperAdmin) y no deben loguearse.
/// </summary>
public sealed record LegalRepresentativeResponse(
    Guid Id,
    Guid RepresentedCompanyId,
    string CompanyDocumentNumber,
    string CompanyName,
    string DocumentType,
    string DocumentNumber,
    string FirstLastName,
    string? SecondLastName,
    string Name,
    string? Email,
    string? Address,
    string? City,
    string? Phone,
    Guid? SignatureVaultId,
    Guid? IdentityValidationRef,
    bool HasSignatureOrIdentity,
    IReadOnlyList<Guid> ProcedureTypeIds,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static LegalRepresentativeResponse From(LegalRepresentativeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new LegalRepresentativeResponse(
            item.Id,
            item.RepresentedCompanyId,
            item.CompanyDocumentNumber,
            item.CompanyName,
            item.DocumentType,
            item.DocumentNumber,
            item.FirstLastName,
            item.SecondLastName,
            item.Name,
            item.Email,
            item.Address,
            item.City,
            item.Phone,
            item.SignatureVaultId,
            item.IdentityValidationRef,
            item.HasSignatureOrIdentity,
            item.ProcedureTypeIds,
            item.IsActive,
            item.CreatedAt,
            item.UpdatedAt);
    }
}

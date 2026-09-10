using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Identity;

namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Vista de un representante legal para la gestión admin (HU #10901). Proyecta los datos denormalizados
/// de la compañía representada, las referencias de firma/identidad vigentes y los tipos de trámite del
/// puente M:N. <c>DocumentNumber</c> (y el NIT de la compañía) son PII (Ley 1581): se entregan solo en
/// respuestas autenticadas de gestión (SuperAdmin) y no deben loguearse.
/// </summary>
public sealed record LegalRepresentativeResponse(
    Guid Id,
    Guid? RepresentedCompanyId,
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
    IReadOnlyList<LegalRepresentativeCompanySummary> Companies,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    // HU #11059 — vigencia de identidad y firma, para poder ofrecer la renovación de lo vencido.
    // Opcionales al final del record para no romper a los consumidores posicionales existentes.
    string IdentityStatus = AdminIdentityVigencia.None,
    DateTimeOffset? IdentityValidUntil = null,
    bool FirmaBaulVigente = false,
    DateOnly? FirmaBaulVigenteHasta = null)
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
            item.Companies,
            item.IsActive,
            item.CreatedAt,
            item.UpdatedAt,
            item.IdentityStatus,
            item.IdentityValidUntil,
            item.FirmaBaulVigente,
            item.FirmaBaulVigenteHasta);
    }
}

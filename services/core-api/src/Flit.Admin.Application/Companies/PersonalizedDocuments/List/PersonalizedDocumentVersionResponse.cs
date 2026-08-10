namespace Flit.Admin.Application.Companies.PersonalizedDocuments.List;

/// <summary>Una versión en el historial (HU #11313, contrato §8 del plan técnico).</summary>
public sealed record PersonalizedDocumentVersionResponse(
    Guid Id,
    int Version,
    string Status,
    string Filename,
    string Sha256,
    int? PageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DeactivatedAt);

/// <summary>Agrupación por tipo de documento: versión vigente (si hay) + historial completo.</summary>
public sealed record PersonalizedDocumentGroupResponse(
    string DocumentType,
    PersonalizedDocumentVersionResponse? Active,
    IReadOnlyList<PersonalizedDocumentVersionResponse> History);

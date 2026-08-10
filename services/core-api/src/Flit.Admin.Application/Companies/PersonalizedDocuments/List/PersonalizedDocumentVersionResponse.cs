namespace Flit.Admin.Application.Companies.PersonalizedDocuments.List;

/// <summary>
/// Una versión en el historial (HU #11313/#11314, contrato §8 del plan técnico). <c>CreatedBy</c> /
/// <c>ActivatedBy</c> responden "quién cargó o reactivó" (AC del historial); <c>IsActive</c> es el
/// indicador de vigencia de ESTA fila (no solo la agregada en <see cref="PersonalizedDocumentGroupResponse.Active"/>).
/// </summary>
public sealed record PersonalizedDocumentVersionResponse(
    Guid Id,
    int Version,
    string Status,
    bool IsActive,
    string Filename,
    string Sha256,
    int? PageCount,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    DateTimeOffset? ActivatedAt,
    Guid? ActivatedBy,
    DateTimeOffset? DeactivatedAt,
    Guid? DeactivatedBy);

/// <summary>Agrupación por tipo de documento: versión vigente (si hay) + historial completo.</summary>
public sealed record PersonalizedDocumentGroupResponse(
    string DocumentType,
    PersonalizedDocumentVersionResponse? Active,
    IReadOnlyList<PersonalizedDocumentVersionResponse> History);

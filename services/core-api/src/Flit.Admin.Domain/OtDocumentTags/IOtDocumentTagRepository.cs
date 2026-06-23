namespace Flit.Admin.Domain.OtDocumentTags;

/// <summary>Repositorio de etiquetas documentales OT — <c>admin.ot_document_tags</c> (HU #10222).</summary>
public interface IOtDocumentTagRepository
{
    Task<IReadOnlyList<OtDocumentTag>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsCodeAsync(
        Guid tenantId,
        string code,
        CancellationToken cancellationToken = default);

    Task<OtDocumentTag> CreateAsync(
        Guid tenantId,
        string code,
        string name,
        string color,
        Guid? createdBy,
        CancellationToken cancellationToken = default);
}

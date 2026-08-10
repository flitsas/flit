namespace Flit.Admin.Application.Companies.PersonalizedDocuments.List;

/// <summary>
/// Lista el historial de documentos personalizados de un tenant, agrupado por tipo (HU #11313). El
/// aislamiento multi-tenant lo aporta <see cref="ICompanyPersonalizedDocumentRepository.ListByTenantAsync"/>
/// con su <c>WHERE tenant_id</c> explícito — el GET no exige el canal <c>TENANT_API</c> (restricción 9:
/// el historial se puede seguir consultando tras volver a <c>FLIT_SMTP</c>).
/// </summary>
public sealed class ListPersonalizedDocumentsHandler
{
    private readonly ICompanyPersonalizedDocumentRepository _repository;

    public ListPersonalizedDocumentsHandler(ICompanyPersonalizedDocumentRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<PersonalizedDocumentGroupResponse>> HandleAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _repository.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return rows
            .GroupBy(r => r.DocumentType, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderByDescending(r => r.Version).ToList();
                var active = ordered.FirstOrDefault(r => r.IsActive);
                return new PersonalizedDocumentGroupResponse(
                    group.Key,
                    active is null ? null : Map(active),
                    ordered.Select(Map).ToList());
            })
            .OrderBy(g => g.DocumentType, StringComparer.Ordinal)
            .ToList();
    }

    private static PersonalizedDocumentVersionResponse Map(CompanyPersonalizedDocumentRecord r) =>
        new(
            r.Id,
            r.Version,
            r.Status,
            r.IsActive,
            r.Filename,
            r.StorageSha256,
            r.PageCount,
            r.CreatedAt,
            r.CreatedBy,
            r.ActivatedAt,
            r.ActivatedBy,
            r.DeactivatedAt,
            r.DeactivatedBy);
}

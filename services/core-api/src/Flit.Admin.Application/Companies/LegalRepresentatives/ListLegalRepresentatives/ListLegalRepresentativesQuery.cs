namespace Flit.Admin.Application.Companies.LegalRepresentatives.ListLegalRepresentatives;

/// <summary>Consulta paginada de representantes legales de un tenant (HU #10901).</summary>
public sealed class ListLegalRepresentativesQuery
{
    public required Guid TenantId { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

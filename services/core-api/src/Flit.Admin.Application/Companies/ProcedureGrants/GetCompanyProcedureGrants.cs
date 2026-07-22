using Flit.Admin.Domain.Companies.ProcedureGrants;

namespace Flit.Admin.Application.Companies.ProcedureGrants;

/// <summary>Consulta de los tipos de trámite habilitados de una compañía.</summary>
public sealed class GetCompanyProcedureGrantsQuery
{
    public required Guid TenantId { get; init; }
}

/// <summary>Respuesta: ids de los tipos de trámite habilitados de la compañía.</summary>
public sealed record CompanyProcedureGrantsResponse(IReadOnlyList<Guid> ProcedureTypeIds);

/// <summary>Caso de uso de lectura de los grants habilitados de una compañía (FEATURE-08).</summary>
public sealed class GetCompanyProcedureGrantsHandler
{
    private readonly ICompanyProcedureGrantRepository _repository;

    public GetCompanyProcedureGrantsHandler(ICompanyProcedureGrantRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CompanyProcedureGrantsResponse> HandleAsync(
        GetCompanyProcedureGrantsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ids = await _repository
            .ListEnabledProcedureTypeIdsAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return new CompanyProcedureGrantsResponse(ids);
    }
}

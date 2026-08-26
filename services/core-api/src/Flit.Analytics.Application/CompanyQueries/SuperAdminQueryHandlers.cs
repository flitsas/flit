using Flit.Queries.Domain;

namespace Flit.Analytics.Application.CompanyQueries;

/// <summary>
/// Ejecuta una consulta de SuperAdmin sobre todas las compañías. Reutiliza el catálogo y el motor
/// de la empresa gestora (<see cref="CompanyQueryFieldCatalog"/>): es el mismo dominio, solo que sin
/// un tenant único — ver <see cref="ICompanyQueryRepository.ExecuteForSuperAdminAsync"/>.
/// </summary>
public sealed class ExecuteSuperAdminQueryHandler(ICompanyQueryRepository repository)
{
    public Task<CompanyQueryResultDto> HandleAsync(
        QueryRequest request,
        CancellationToken cancellationToken = default) =>
        repository.ExecuteForSuperAdminAsync(request, cancellationToken);

    public static QueryRequest BuildRequest(QueryDefinition? definition, int? page, int? pageSize) =>
        QueryNormalizer.BuildRequest(CompanyQueryFieldCatalog.Instance, definition, page, pageSize);
}

/// <summary>Catálogo de campos, con «Compañía» resuelta a las compañías activas de la plataforma.</summary>
public sealed class GetSuperAdminQueryFieldsHandler(ICompanyQueryRepository repository)
{
    public Task<IReadOnlyList<QueryFieldDto>> HandleAsync(CancellationToken cancellationToken = default) =>
        repository.GetFieldsForSuperAdminAsync(cancellationToken);
}

public sealed class ListSuperAdminSavedQueriesHandler(ISuperAdminSavedQueryRepository repository)
{
    public Task<IReadOnlyList<SavedQueryDto>> HandleAsync(CancellationToken cancellationToken = default) =>
        repository.ListAsync(cancellationToken);
}

public sealed class SaveSuperAdminQueryHandler(ISuperAdminSavedQueryRepository repository)
{
    public Task<SavedQueryDto> HandleAsync(
        Guid userId,
        Guid? id,
        SavedQueryInput input,
        CancellationToken cancellationToken = default) =>
        repository.SaveAsync(userId, id, input, cancellationToken);

    /// <inheritdoc cref="SavedQuery.BuildInput"/>
    public static SavedQueryInput BuildInput(
        string? nombre, string? descripcion, QueryDefinition? definition) =>
        SavedQuery.BuildInput(CompanyQueryFieldCatalog.Instance, nombre, descripcion, definition);
}

public sealed class DeleteSuperAdminSavedQueryHandler(ISuperAdminSavedQueryRepository repository)
{
    public Task<bool> HandleAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.DeleteAsync(id, cancellationToken);
}

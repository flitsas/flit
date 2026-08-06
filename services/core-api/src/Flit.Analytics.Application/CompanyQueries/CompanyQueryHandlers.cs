using Flit.Queries.Domain;

namespace Flit.Analytics.Application.CompanyQueries;

/// <summary>
/// Ejecuta una consulta armada por el usuario de la empresa.
///
/// <para>La normalización ocurre siempre, aquí y en el repositorio: lo que llega de la red es una
/// propuesta, no una definición. Un campo que no existe, un operador que ese campo no admite o una
/// lista de mil placas se recortan a algo válido antes de tocar la base.</para>
/// </summary>
public sealed class ExecuteCompanyQueryHandler(ICompanyQueryRepository repository)
{
    public Task<CompanyQueryResultDto> HandleAsync(
        Guid tenantId,
        QueryRequest request,
        CancellationToken cancellationToken = default) =>
        repository.ExecuteAsync(tenantId, request, cancellationToken);

    public static QueryRequest BuildRequest(QueryDefinition? definition, int? page, int? pageSize) =>
        QueryNormalizer.BuildRequest(CompanyQueryFieldCatalog.Instance, definition, page, pageSize);
}

/// <summary>Catálogo de campos consultables, con las opciones de la empresa ya resueltas.</summary>
public sealed class GetCompanyQueryFieldsHandler(ICompanyQueryRepository repository)
{
    public Task<IReadOnlyList<QueryFieldDto>> HandleAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        repository.GetFieldsAsync(tenantId, cancellationToken);
}

public sealed class ListCompanySavedQueriesHandler(ICompanyQueryRepository repository)
{
    public Task<IReadOnlyList<SavedQueryDto>> HandleAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        repository.ListSavedAsync(tenantId, userId, cancellationToken);
}

public sealed class SaveCompanyQueryHandler(ICompanyQueryRepository repository)
{
    public Task<SavedQueryDto> HandleAsync(
        Guid tenantId,
        Guid userId,
        Guid? id,
        SavedQueryInput input,
        CancellationToken cancellationToken = default) =>
        repository.SaveAsync(tenantId, userId, id, input, cancellationToken);

    /// <inheritdoc cref="SavedQuery.BuildInput"/>
    public static SavedQueryInput BuildInput(
        string? nombre, string? descripcion, QueryDefinition? definition) =>
        SavedQuery.BuildInput(CompanyQueryFieldCatalog.Instance, nombre, descripcion, definition);
}

public sealed class DeleteCompanySavedQueryHandler(ICompanyQueryRepository repository)
{
    public Task<bool> HandleAsync(
        Guid tenantId,
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        repository.DeleteSavedAsync(tenantId, userId, id, cancellationToken);
}

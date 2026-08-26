using Flit.Queries.Domain;

namespace Flit.Analytics.Application.IctQueries;

/// <summary>
/// Ejecuta una consulta armada por el usuario sobre sus pre-trámites de ICT.
///
/// <para>La normalización ocurre siempre, aquí y en el repositorio: lo que llega de la red es una
/// propuesta, no una definición. Un campo que no existe, un operador que ese campo no admite o una
/// lista de mil placas se recortan a algo válido antes de tocar la base.</para>
/// </summary>
public sealed class ExecuteIctQueryHandler(IIctQueryRepository repository)
{
    public Task<IctQueryResultDto> HandleAsync(
        Guid tenantId,
        QueryRequest request,
        CancellationToken cancellationToken = default) =>
        repository.ExecuteAsync(tenantId, request, cancellationToken);

    public static QueryRequest BuildRequest(QueryDefinition? definition, int? page, int? pageSize) =>
        QueryNormalizer.BuildRequest(IctQueryFieldCatalog.Instance, definition, page, pageSize);
}

/// <summary>Catálogo de campos consultables, con las opciones de la empresa ya resueltas.</summary>
public sealed class GetIctQueryFieldsHandler(IIctQueryRepository repository)
{
    public Task<IReadOnlyList<QueryFieldDto>> HandleAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        repository.GetFieldsAsync(tenantId, cancellationToken);
}

public sealed class ListIctSavedQueriesHandler(IIctQueryRepository repository)
{
    public Task<IReadOnlyList<SavedQueryDto>> HandleAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        repository.ListSavedAsync(tenantId, userId, cancellationToken);
}

public sealed class SaveIctQueryHandler(IIctQueryRepository repository)
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
        SavedQuery.BuildInput(IctQueryFieldCatalog.Instance, nombre, descripcion, definition);
}

public sealed class DeleteIctSavedQueryHandler(IIctQueryRepository repository)
{
    public Task<bool> HandleAsync(
        Guid tenantId,
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        repository.DeleteSavedAsync(tenantId, userId, id, cancellationToken);
}

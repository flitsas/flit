using Flit.Admin.Domain.OtQueries;
using Flit.Queries.Domain;

namespace Flit.Admin.Application.OtQueries;

/// <summary>
/// Ejecuta una consulta armada por el usuario.
///
/// <para>La normalización ocurre siempre, aquí y en el repositorio: lo que llega de la red es una
/// propuesta, no una definición. Un campo que no existe, un operador que ese campo no admite o una
/// lista de mil placas se recortan a algo válido antes de tocar la base.</para>
/// </summary>
public sealed class ExecuteOtQueryHandler(IOtQueryRepository repository)
{
    public Task<OtQueryResultDto?> HandleAsync(
        Guid otTenantId,
        QueryRequest request,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.ExecuteAsync(otTenantId, request, transitOfficeIdOverride, cancellationToken);

    public static QueryRequest BuildRequest(QueryDefinition? definition, int? page, int? pageSize) =>
        QueryNormalizer.BuildRequest(OtQueryFieldCatalog.Instance, definition, page, pageSize);
}

/// <summary>Catálogo de campos consultables, con las opciones del organismo ya resueltas.</summary>
public sealed class GetOtQueryFieldsHandler(IOtQueryRepository repository)
{
    public Task<IReadOnlyList<QueryFieldDto>?> HandleAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.GetFieldsAsync(otTenantId, transitOfficeIdOverride, cancellationToken);
}

public sealed class ListOtSavedQueriesHandler(IOtQueryRepository repository)
{
    public Task<IReadOnlyList<SavedQueryDto>?> HandleAsync(
        Guid otTenantId,
        Guid userId,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.ListSavedAsync(otTenantId, userId, transitOfficeIdOverride, cancellationToken);
}

public sealed class SaveOtQueryHandler(IOtQueryRepository repository)
{
    public Task<SavedQueryDto?> HandleAsync(
        Guid otTenantId,
        Guid userId,
        Guid? id,
        SavedQueryInput input,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.SaveAsync(otTenantId, userId, id, input, transitOfficeIdOverride, cancellationToken);

    /// <summary>
    /// Un nombre vacío no se rechaza con un error: se pone uno. Que la consulta se guarde es más
    /// importante que cómo se llama, y renombrarla es un clic.
    /// </summary>
    public static SavedQueryInput BuildInput(string? nombre, string? descripcion, QueryDefinition? definition) =>
        SavedQuery.BuildInput(OtQueryFieldCatalog.Instance, nombre, descripcion, definition);
}

public sealed class DeleteOtSavedQueryHandler(IOtQueryRepository repository)
{
    public Task<bool> HandleAsync(
        Guid otTenantId,
        Guid userId,
        Guid id,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.DeleteSavedAsync(otTenantId, userId, id, transitOfficeIdOverride, cancellationToken);
}

using Flit.Admin.Domain.OtQueries;

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
        OtQueryRequest request,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.ExecuteAsync(otTenantId, request, transitOfficeIdOverride, cancellationToken);

    public static OtQueryRequest BuildRequest(OtQueryDefinition? definition, int? page, int? pageSize) =>
        new(
            OtQueryFieldCatalog.Normalize(definition),
            Math.Max(1, page ?? 1),
            Math.Clamp(pageSize ?? OtQueryLimits.DefaultPageSize, 1, OtQueryLimits.MaxPageSize));
}

/// <summary>Catálogo de campos consultables, con las opciones del organismo ya resueltas.</summary>
public sealed class GetOtQueryFieldsHandler(IOtQueryRepository repository)
{
    public Task<IReadOnlyList<OtQueryFieldDto>?> HandleAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.GetFieldsAsync(otTenantId, transitOfficeIdOverride, cancellationToken);
}

public sealed class ListOtSavedQueriesHandler(IOtQueryRepository repository)
{
    public Task<IReadOnlyList<OtSavedQueryDto>?> HandleAsync(
        Guid otTenantId,
        Guid userId,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.ListSavedAsync(otTenantId, userId, transitOfficeIdOverride, cancellationToken);
}

public sealed class SaveOtQueryHandler(IOtQueryRepository repository)
{
    public Task<OtSavedQueryDto?> HandleAsync(
        Guid otTenantId,
        Guid userId,
        Guid? id,
        OtSavedQueryInput input,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.SaveAsync(otTenantId, userId, id, input, transitOfficeIdOverride, cancellationToken);

    /// <summary>
    /// Un nombre vacío no se rechaza con un error: se pone uno. Que la consulta se guarde es más
    /// importante que cómo se llama, y renombrarla es un clic.
    /// </summary>
    public static OtSavedQueryInput BuildInput(string? nombre, string? descripcion, OtQueryDefinition? definition) =>
        new(
            string.IsNullOrWhiteSpace(nombre) ? "Consulta sin nombre" : nombre.Trim()[..Math.Min(nombre.Trim().Length, 120)],
            descripcion,
            OtQueryFieldCatalog.Normalize(definition));
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

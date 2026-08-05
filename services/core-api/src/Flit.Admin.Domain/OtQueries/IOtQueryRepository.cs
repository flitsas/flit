namespace Flit.Admin.Domain.OtQueries;

/// <summary>
/// Lectura y persistencia de las consultas del organismo.
///
/// <para>Devuelve <c>null</c> cuando el tenant no tiene organismo asociado, igual que el resto de
/// reportes OT: es la señal para responder 404 con el motivo en vez de un resultado vacío que se
/// leería como «no hay trámites».</para>
/// </summary>
public interface IOtQueryRepository
{
    /// <summary>Ejecuta una consulta ya normalizada y devuelve una página del resultado.</summary>
    Task<OtQueryResultDto?> ExecuteAsync(
        Guid otTenantId,
        OtQueryRequest request,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Catálogo de campos con las opciones que dependen del organismo (empresas, revisores) ya
    /// rellenadas.
    /// </summary>
    Task<IReadOnlyList<OtQueryFieldDto>?> GetFieldsAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>Las del usuario más las de fábrica, éstas siempre al final.</summary>
    Task<IReadOnlyList<OtSavedQueryDto>?> ListSavedAsync(
        Guid otTenantId,
        Guid userId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    Task<OtSavedQueryDto?> SaveAsync(
        Guid otTenantId,
        Guid userId,
        Guid? id,
        OtSavedQueryInput input,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary><c>false</c> si no existe o no es del usuario.</summary>
    Task<bool> DeleteSavedAsync(
        Guid otTenantId,
        Guid userId,
        Guid id,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);
}

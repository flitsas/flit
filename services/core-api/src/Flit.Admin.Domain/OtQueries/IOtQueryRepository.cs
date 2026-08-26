using Flit.Queries.Domain;

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
        QueryRequest request,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Catálogo de campos con las opciones que dependen del organismo (empresas, revisores) ya
    /// rellenadas.
    /// </summary>
    Task<IReadOnlyList<QueryFieldDto>?> GetFieldsAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>Las del usuario más las de fábrica, éstas siempre al final.</summary>
    Task<IReadOnlyList<SavedQueryDto>?> ListSavedAsync(
        Guid otTenantId,
        Guid userId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Una guardada por id, SIN filtrar por usuario — la usa el scheduler de informes programados
    /// (Reportes 2.0, HU-D), que corre sin un usuario "actual" en contexto. Incluye las de fábrica.
    /// Null si no existe o es de otro organismo.
    /// </summary>
    Task<SavedQueryDto?> GetSavedByIdAsync(
        Guid otTenantId,
        Guid id,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    Task<SavedQueryDto?> SaveAsync(
        Guid otTenantId,
        Guid userId,
        Guid? id,
        SavedQueryInput input,
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

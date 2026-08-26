using Flit.Queries.Domain;

namespace Flit.Analytics.Application.IctQueries;

/// <summary>
/// Lectura y persistencia de las consultas sobre pre-trámites de ICT.
///
/// <para>Como con la empresa, no hay caso «este tenant no tiene el módulo»: toda compañía con
/// integración ICT tiene sus propios pre-trámites, aunque sean cero. Ningún método devuelve
/// <c>null</c> como señal de 404 — una compañía sin pre-trámites responde una página vacía.</para>
/// </summary>
public interface IIctQueryRepository
{
    /// <summary>Ejecuta una consulta ya normalizada y devuelve una página del resultado.</summary>
    Task<IctQueryResultDto> ExecuteAsync(
        Guid tenantId,
        QueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Catálogo de campos con las opciones que dependen de los datos de la empresa —tipos de
    /// trámite, secretarías, clientes de integración— ya rellenadas con lo que esa empresa
    /// realmente tiene.
    /// </summary>
    Task<IReadOnlyList<QueryFieldDto>> GetFieldsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Las del usuario más las de fábrica, éstas siempre al final.</summary>
    Task<IReadOnlyList<SavedQueryDto>> ListSavedAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Una guardada por id, SIN filtrar por el usuario que la creó — para cuando esta consulta se
    /// programe como informe periódico (HU #11609), que corre sin un usuario "actual" en contexto.
    /// Incluye las de fábrica. Null si no existe o es de otro tenant.
    /// </summary>
    Task<SavedQueryDto?> GetSavedByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<SavedQueryDto> SaveAsync(
        Guid tenantId,
        Guid userId,
        Guid? id,
        SavedQueryInput input,
        CancellationToken cancellationToken = default);

    /// <summary><c>false</c> si no existe o no es del usuario.</summary>
    Task<bool> DeleteSavedAsync(
        Guid tenantId,
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default);
}

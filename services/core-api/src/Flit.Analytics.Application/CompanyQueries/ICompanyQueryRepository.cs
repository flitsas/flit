using Flit.Queries.Domain;

namespace Flit.Analytics.Application.CompanyQueries;

/// <summary>
/// Lectura y persistencia de las consultas de la empresa gestora.
///
/// <para>A diferencia del organismo, aquí no hay caso «este tenant no tiene el módulo configurado»:
/// toda empresa tiene sus propios trámites, aunque sean cero. Por eso ningún método devuelve
/// <c>null</c> como señal de 404 — una empresa sin trámites responde una página vacía, que es la
/// verdad.</para>
/// </summary>
public interface ICompanyQueryRepository
{
    /// <summary>Ejecuta una consulta ya normalizada y devuelve una página del resultado.</summary>
    Task<CompanyQueryResultDto> ExecuteAsync(
        Guid tenantId,
        QueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Catálogo de campos con las opciones que dependen de los datos de la empresa —organismos,
    /// tipos de trámite, quién radica, métodos de pago— ya rellenadas con lo que esa empresa
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

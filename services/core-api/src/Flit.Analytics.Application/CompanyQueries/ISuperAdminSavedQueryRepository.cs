using Flit.Queries.Domain;

namespace Flit.Analytics.Application.CompanyQueries;

/// <summary>
/// Lectura y persistencia de las consultas guardadas de SuperAdmin en modo «todas las compañías».
///
/// <para>Sin alcance de tenant ni de usuario: cualquier SuperAdmin ve, guarda y borra estas
/// consultas — es la decisión de producto (equipo de operaciones compartiendo consultas), no un
/// descuido. <c>userId</c> solo se usa para dejar constancia de quién creó cada una.</para>
/// </summary>
public interface ISuperAdminSavedQueryRepository
{
    /// <summary>Todas las guardadas por cualquier SuperAdmin, más las de fábrica al final.</summary>
    Task<IReadOnlyList<SavedQueryDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Una guardada por id (incluye las de fábrica). La usa el scheduler de informes programados
    /// (Reportes 2.0, HU-D) tipo "consulta" con alcance superadmin. Null si no existe.
    /// </summary>
    Task<SavedQueryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary><paramref name="id"/> nulo crea; con id, actualiza la que exista (de cualquier autor).</summary>
    Task<SavedQueryDto> SaveAsync(
        Guid userId,
        Guid? id,
        SavedQueryInput input,
        CancellationToken cancellationToken = default);

    /// <summary><c>false</c> si no existe.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

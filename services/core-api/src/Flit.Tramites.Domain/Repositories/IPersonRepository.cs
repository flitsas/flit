using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Repositorio mínimo de la entidad <see cref="Person"/> — HU #10865 (CF-00, ADR-0030).
/// La implementación vive en <c>Flit.Infrastructure</c>; esta interfaz es el puerto del dominio.
/// </summary>
public interface IPersonRepository
{
    /// <summary>
    /// Resuelve una persona por su clave de negocio <c>(tenantId, documentType, documentNumber)</c>.
    /// Devuelve <c>null</c> si no existe o está soft-deleted. Solo lectura.
    /// </summary>
    Task<Person?> FindByDocumentAsync(
        Guid tenantId, string documentType, string documentNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Agrega una nueva persona al contexto (sin commit). El caller debe invocar
    /// <c>SaveChangesAsync</c> en la unidad de trabajo correspondiente.
    /// </summary>
    Task AddAsync(Person person, CancellationToken ct = default);

    /// <summary>
    /// Persiste la unidad de trabajo actual.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>Persistencia append-only de validaciones OT de firma de impronta (HU #12148 / #12176).</summary>
public interface IImprintSignatureValidationRepository
{
    void Add(ImprintSignatureValidation row);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Última validación por cada impronta (mayor <c>validated_at</c>).</summary>
    Task<IReadOnlyDictionary<Guid, ImprintSignatureValidation>> GetLatestByImprintIdsAsync(
        IReadOnlyCollection<Guid> imprintIds,
        CancellationToken cancellationToken = default);

    /// <summary>Historial completo de una impronta, más reciente primero.</summary>
    Task<IReadOnlyList<ImprintSignatureValidation>> ListByImprintIdAsync(
        Guid vehicleSignatureImprintId,
        CancellationToken cancellationToken = default);
}

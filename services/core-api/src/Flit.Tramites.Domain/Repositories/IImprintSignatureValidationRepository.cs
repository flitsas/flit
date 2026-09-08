using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>Persistencia append-only de validaciones OT de firma de impronta (HU #12148).</summary>
public interface IImprintSignatureValidationRepository
{
    void Add(ImprintSignatureValidation row);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

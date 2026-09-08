using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>Persistencia de auditoría de improntas firmadas (HU #12116).</summary>
public interface IVehicleSignatureImprintRepository
{
    void Add(VehicleSignatureImprint row);

    Task<VehicleSignatureImprint?> FindByDocumentHashAsync(
        string documentHash,
        CancellationToken cancellationToken = default);
}

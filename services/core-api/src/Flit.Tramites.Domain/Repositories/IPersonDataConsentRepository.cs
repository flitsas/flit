using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Persistencia del gate de consentimiento Habeas Data para el reúso cross-trámite de datos de
/// persona (HU #10878, ADR-0031). Aislamiento por tenant en el <c>WHERE</c>; RLS como defensa en
/// profundidad. <see cref="GetAsync"/> devuelve la fila TRACKEADA (el llamador puede mutarla
/// directamente para un upsert de <c>granted</c>, ver <c>PutActorsHandler</c>).
/// </summary>
public interface IPersonDataConsentRepository
{
    Task<PersonDataConsent?> GetAsync(
        Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default);

    Task AddAsync(PersonDataConsent consent, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistencia del gate de consentimiento Habeas Data (HU #10878, ADR-0031). Aislamiento por
/// tenant en el <c>WHERE</c>; RLS de la tabla como defensa en profundidad.
/// </summary>
internal sealed class PersonDataConsentRepository(FlitDbContext db) : IPersonDataConsentRepository
{
    public Task<PersonDataConsent?> GetAsync(
        Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default) =>
        db.PersonDataConsents.FirstOrDefaultAsync(
            x => x.TenantId == tenantId
                && x.DocumentType == documentType
                && x.DocumentNumber == documentNumber,
            ct);

    public async Task AddAsync(PersonDataConsent consent, CancellationToken ct = default) =>
        await db.PersonDataConsents.AddAsync(consent, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}

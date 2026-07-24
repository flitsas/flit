using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistencia de la entidad <see cref="Person"/> — HU #10865 (Feature #10864, CF-00, ADR-0030).
/// Aislamiento por tenant explícito en el WHERE; la RLS de <c>tramites.persons</c> es defensa
/// en profundidad (checklist §B4/§B5).
/// </summary>
internal sealed class PersonRepository(FlitDbContext db) : IPersonRepository
{
    public Task<Person?> FindByDocumentAsync(
        Guid tenantId, string documentType, string documentNumber,
        CancellationToken ct = default) =>
        db.Persons
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId
                    && x.DocumentType == documentType
                    && x.DocumentNumber == documentNumber
                    && x.DeletedAt == null,
                ct);

    public async Task AddAsync(Person person, CancellationToken ct = default) =>
        await db.Persons.AddAsync(person, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}

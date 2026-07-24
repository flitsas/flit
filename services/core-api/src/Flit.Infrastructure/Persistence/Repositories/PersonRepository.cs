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

    /// <inheritdoc/>
    public async Task<Person> FindOrCreateAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        string fullName,
        string email,
        string personType,
        string? legalRepDocumentType,
        string? legalRepDocumentNumber,
        string? legalRepName,
        string? legalRepEmail,
        CancellationToken ct = default)
    {
        var person = await db.Persons
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId
                    && x.DocumentType == documentType
                    && x.DocumentNumber == documentNumber
                    && x.DeletedAt == null,
                ct);

        var now = DateTimeOffset.UtcNow;

        if (person is null)
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentType = documentType.Trim(),
                DocumentNumber = documentNumber.Trim(),
                FullName = fullName.Trim(),
                Email = email.Trim(),
                PersonType = personType,
                LegalRepDocumentType = legalRepDocumentType?.Trim(),
                LegalRepDocumentNumber = legalRepDocumentNumber?.Trim(),
                LegalRepName = legalRepName?.Trim(),
                LegalRepEmail = legalRepEmail?.Trim(),
                CreatedAt = now,
                RowVersion = 1,
            };
            await db.Persons.AddAsync(person, ct);
        }
        else
        {
            person.FullName = fullName.Trim();
            person.Email = email.Trim();
            person.PersonType = personType;
            person.LegalRepDocumentType = legalRepDocumentType?.Trim();
            person.LegalRepDocumentNumber = legalRepDocumentNumber?.Trim();
            person.LegalRepName = legalRepName?.Trim();
            person.LegalRepEmail = legalRepEmail?.Trim();
            person.UpdatedAt = now;
        }

        return person;
    }

    /// <inheritdoc/>
    public Task<ProcedureInstanceBiometricValidation?> FindActiveStandaloneValidationAsync(
        Guid personId, CancellationToken ct = default) =>
        db.Set<ProcedureInstanceBiometricValidation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                v => v.PersonId == personId
                    && v.ProcedureInstanceId == null
                    && (v.Status == BiometricEstados.Enviado
                        || v.Status == BiometricEstados.EnProceso
                        || v.Status == BiometricEstados.PendienteEnvio),
                ct);

    public async Task AddAsync(Person person, CancellationToken ct = default) =>
        await db.Persons.AddAsync(person, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}

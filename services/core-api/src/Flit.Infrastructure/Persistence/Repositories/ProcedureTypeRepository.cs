using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class ProcedureTypeRepository(FlitDbContext db) : IProcedureTypeRepository
{
    public Task<ProcedureType?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.ProcedureTypes.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<ProcedureType?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct) =>
        db.ProcedureTypes
            .Include(x => x.ConformationRules).ThenInclude(r => r.ProcedureEntity)
            .Include(x => x.Steps).ThenInclude(s => s.Sections)
                .ThenInclude(sec => sec.FormFields)
                    .ThenInclude(f => f.ConsultationTemplate)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<ProcedureType?> GetByCodePublishedAsync(string code, CancellationToken ct) =>
        db.ProcedureTypes
            .Include(x => x.ConformationRules).ThenInclude(r => r.ProcedureEntity)
            .Include(x => x.Steps).ThenInclude(s => s.Sections)
                .ThenInclude(sec => sec.FormFields)
            .FirstOrDefaultAsync(x => x.Code == code && x.PublicationStatus == PublicationStatus.Published, ct);

    public async Task<List<ProcedureType>> ListAsync(
        string? family, string? publicationStatus, CancellationToken ct)
    {
        var query = db.ProcedureTypes.AsQueryable();
        if (!string.IsNullOrEmpty(family))
            query = query.Where(x => x.Family == family);
        if (!string.IsNullOrEmpty(publicationStatus))
            query = query.Where(x => x.PublicationStatus == publicationStatus);

        return await query.OrderBy(x => x.Code).ToListAsync(ct);
    }

    public async Task AddAsync(ProcedureType procedureType, CancellationToken ct)
    {
        await db.ProcedureTypes.AddAsync(procedureType, ct);
    }

    public Task UpdateAsync(ProcedureType procedureType, CancellationToken ct)
    {
        db.ProcedureTypes.Update(procedureType);
        return Task.CompletedTask;
    }

    public Task<List<ConformationRule>> GetConformationRulesAsync(Guid procedureTypeId, CancellationToken ct) =>
        db.ConformationRules
            .Include(r => r.ProcedureEntity)
            .Where(r => r.ProcedureTypeId == procedureTypeId)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);

    public async Task ReplaceConformationRulesAsync(
        Guid procedureTypeId,
        List<ConformationRule> rules,
        CancellationToken ct)
    {
        var existing = await db.ConformationRules
            .Where(r => r.ProcedureTypeId == procedureTypeId)
            .ToListAsync(ct);

        db.ConformationRules.RemoveRange(existing);
        await db.ConformationRules.AddRangeAsync(rules, ct);
    }

    public Task<List<ProcedureStep>> GetStepsWithDetailsAsync(Guid procedureTypeId, CancellationToken ct) =>
        db.ProcedureSteps
            .Include(s => s.Sections)
                .ThenInclude(sec => sec.FormFields)
                    .ThenInclude(f => f.ConsultationTemplate)
            .Where(s => s.ProcedureTypeId == procedureTypeId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

    public async Task ReplaceStepsAsync(
        Guid procedureTypeId,
        List<ProcedureStep> steps,
        CancellationToken ct)
    {
        var existing = await db.ProcedureSteps
            .Where(s => s.ProcedureTypeId == procedureTypeId)
            .ToListAsync(ct);

        db.ProcedureSteps.RemoveRange(existing);
        await db.ProcedureSteps.AddRangeAsync(steps, ct);
    }

    public Task AddFormFieldsAsync(IEnumerable<FormField> fields, CancellationToken ct) =>
        db.Set<FormField>().AddRangeAsync(fields, ct);

    /// <summary>
    /// ¿El tipo tiene expedientes? Es la guarda que impide archivar un tipo que alguien está usando.
    /// <para>Devolvía <c>false</c> constante —un stub—, así que la guarda nunca se disparaba y
    /// cualquier tipo se podía archivar con trámites vivos colgando. Los borrados lógicos no cuentan:
    /// un expediente eliminado ya no ata al tipo.</para>
    /// </summary>
    public Task<bool> HasInstancesAsync(Guid procedureTypeId, CancellationToken ct) =>
        db.ProcedureInstances
            .AsNoTracking()
            .AnyAsync(i => i.ProcedureTypeId == procedureTypeId && i.DeletedAt == null, ct);

    /// <summary>
    /// ¿Ya existe un tipo con este código? El código es la llave del catálogo —lo usan las
    /// integraciones y los snapshots congelados—, y la base tiene un UNIQUE. Sin esta comprobación
    /// el alta desde el configurador moría con un 500 del constraint en vez de decir qué pasó.
    /// Incluye archivados a propósito: su código sigue ocupado.
    /// </summary>
    public Task<bool> CodeExistsAsync(string code, CancellationToken ct) =>
        db.ProcedureTypes
            .AsNoTracking()
            .AnyAsync(t => t.Code == code, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct).ContinueWith(_ => { }, ct);
}

using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de los requisitos documentales por tipo de trámite para el configurador dinámico
/// (FEATURE-08 / CFD-06). Catálogo global sin <c>tenant_id</c>. El código del tipo de documento se
/// resuelve por join contra <c>tramites.document_types</c>. La columna persistida <c>is_mandatory</c>
/// mapea al concepto funcional <c>isRequired</c> del perfil de conformación.
/// <para>Independiente de <c>ProcedureDocumentRequirementRepository</c> (CRUD Admin, HU #10195):
/// aquí se hace reemplazo en bloque con <c>is_dummy</c>/<c>condition_group</c>.</para>
/// </summary>
internal sealed class ProcedureTypeDocumentRepository(FlitDbContext db) : IProcedureTypeDocumentRepository
{
    public async Task<IReadOnlyList<ProcedureDocumentRequirementRecord>> ListByTypeAsync(
        Guid procedureTypeId, CancellationToken ct)
    {
        return await db.ProcedureDocumentRequirements
            .AsNoTracking()
            .Where(r => r.ProcedureTypeId == procedureTypeId)
            .OrderBy(r => r.DefaultSortOrder)
            .Join(
                db.DocumentTypes.AsNoTracking(),
                r => r.DocumentTypeId,
                dt => dt.Id,
                (r, dt) => new ProcedureDocumentRequirementRecord(
                    dt.Code, r.IsMandatory, r.IsDummy, r.ConditionGroup, r.DefaultSortOrder))
            .ToListAsync(ct);
    }

    public async Task<Guid?> ResolveDocumentTypeIdAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmed = code.Trim();
        return await db.DocumentTypes
            .AsNoTracking()
            .Where(d => d.Code == trimmed)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task ReplaceRequirementsAsync(
        Guid procedureTypeId, IReadOnlyList<ProcedureDocumentRequirementUpsert> requirements, CancellationToken ct)
    {
        var existing = await db.ProcedureDocumentRequirements
            .Where(r => r.ProcedureTypeId == procedureTypeId)
            .ToListAsync(ct);
        db.ProcedureDocumentRequirements.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        foreach (var req in requirements)
        {
            db.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = procedureTypeId,
                DocumentTypeId = req.DocumentTypeId,
                IsMandatory = req.IsRequired,
                IsDummy = req.IsDummy,
                ConditionGroup = req.ConditionGroup,
                DefaultSortOrder = (short)req.SortOrder,
                CreatedAt = now,
            });
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct).ContinueWith(_ => { }, ct);
}

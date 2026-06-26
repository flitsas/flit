using System.Globalization;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class ProcedureInstanceRepository(FlitDbContext db) : IProcedureInstanceRepository
{
    private const string ReferenceUniqueConstraint = "uq_procedure_instances_tenant_reference";
    private const int MaxReferenceRetries = 5;
    public Task<ProcedureInstance?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithDetailsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.FieldValues)
            .Include(x => x.StatusHistory)
            .Include(x => x.Actors)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithActorsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.Actors)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithAttachmentsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithWizardGraphAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .AsSplitQuery()
            .Include(x => x.FieldValues)
            .Include(x => x.Actors)
            .Include(x => x.Attachments)
            .Include(x => x.Commercial)
            .Include(x => x.PreflightSnapshots)
            .Include(x => x.BiometricValidations)
            .Include(x => x.Signatures)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithSignaturesAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.Signatures)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithFurGraphAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.FieldValues)
            .Include(x => x.Actors)
            .Include(x => x.Attachments)
            .Include(x => x.Commercial)
            .Include(x => x.BiometricValidations)
            .Include(x => x.Signatures)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public async Task<IReadOnlyList<ProcedureInstance>> ListDraftFinalizedByActorAsync(
        Guid tenantId, string parte, string tipoDoc, string documento, CancellationToken ct)
    {
        return await db.ProcedureInstances
            .Include(i => i.Actors)
            .Where(i => i.TenantId == tenantId
                && i.Status == Flit.Tramites.Domain.Enums.ProcedureInstanceStatus.Draft
                && i.DraftFinalizedAt != null
                && i.DeletedAt == null
                && i.Actors.Any(a =>
                    a.ActorType == parte
                    && a.DocumentType == tipoDoc
                    && a.DocumentNumber == documento))
            .OrderBy(i => i.DraftFinalizedAt)
            .ThenBy(i => i.ReferenceNumber)
            .ToListAsync(ct);
    }

    public Task<ProcedureInstance?> GetByIdWithCommercialAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.Commercial)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public async Task<IReadOnlyList<ProcedureInstance>> ListByTenantWithSummaryGraphAsync(Guid tenantId, int limit, CancellationToken ct)
    {
        var rows = await db.ProcedureInstances
            .AsSplitQuery()
            .Include(x => x.FieldValues)
            .Include(x => x.Actors)
            .Include(x => x.Attachments)
            .Include(x => x.Commercial)
            .Include(x => x.PreflightSnapshots)
            .Include(x => x.BiometricValidations)
            .Include(x => x.Signatures)
            .Where(x => x.TenantId == tenantId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return rows;
    }

    public Task<ProcedureInstance?> GetByIdWithBiometricsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.BiometricValidations)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithBiometricsAndActorsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.BiometricValidations)
            .Include(x => x.Actors)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public async Task<ProcedureInstanceBiometricValidation?> FindVigenteApprovedByDocumentAsync(
        Guid tenantId, string tipoDoc, string documento, DateTimeOffset now, CancellationToken ct)
    {
        // Filtro grueso en SQL por timestamp (validado_at >= corte), con un día de margen para no
        // descartar candidatos cerca del límite; el corte fino por DÍA calendario se aplica en memoria
        // con BiometricRules.EsAprobadaVigente (semántica "día de aprobación = día 1; vence el día 31").
        var cutoff = now.AddDays(-(BiometricRules.VigenciaDias + 1));
        var candidates = await db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId
                && v.Estado == BiometricEstados.Aprobado
                && v.ValidadoAt != null
                && v.ValidadoAt >= cutoff
                && v.TipoDoc == tipoDoc
                && v.Documento == documento
                && v.ProcedureInstance != null
                && v.ProcedureInstance.DeletedAt == null)
            .OrderByDescending(v => v.ValidadoAt)
            .Take(10)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(v => BiometricRules.EsAprobadaVigente(v, now));
    }

    public async Task<IReadOnlyList<ProcedureInstanceBiometricValidation>> ListBiometricValidationsByTenantAsync(Guid tenantId, int limit, CancellationToken ct)
    {
        return await db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Include(v => v.ProcedureInstance)
            .Where(v => v.TenantId == tenantId
                && v.ProcedureInstance != null
                && v.ProcedureInstance.DeletedAt == null)
            .OrderByDescending(v => v.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountBiometricValidationsByEstadoAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId
                && v.ProcedureInstance != null
                && v.ProcedureInstance.DeletedAt == null)
            .GroupBy(v => v.Estado)
            .Select(g => new { Estado = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.Estado, x => x.Count);
    }

    public Task<ProcedureInstanceBiometricValidation?> GetBiometricByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        db.ProcedureInstanceBiometricValidations
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

    public Task<ProcedureInstanceBiometricValidation?> GetBiometricByIdAsync(Guid id, CancellationToken ct) =>
        db.ProcedureInstanceBiometricValidations
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<ProcedureInstance?> GetByIdWithParticipantsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstanceParticipant?> GetParticipantByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        db.ProcedureInstanceParticipants
            .Include(x => x.ProcedureInstance!).ThenInclude(i => i.BiometricValidations)
            .Include(x => x.ProcedureInstance!).ThenInclude(i => i.Signatures)
            .Include(x => x.ProcedureInstance!).ThenInclude(i => i.Attachments)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

    public async Task AddEventAsync(ProcedureInstanceEvent evt, CancellationToken ct) =>
        await db.ProcedureInstanceEvents.AddAsync(evt, ct);

    public Task<ProcedureInstancePreflightSnapshot?> GetLatestPreflightAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstancePreflightSnapshots
            .Where(x => x.ProcedureInstanceId == id && x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddPreflightSnapshotAsync(ProcedureInstancePreflightSnapshot snapshot, CancellationToken ct) =>
        await db.ProcedureInstancePreflightSnapshots.AddAsync(snapshot, ct);

    public Task<int> CountByTenantAndYearAsync(Guid tenantId, int year, CancellationToken ct) =>
        db.ProcedureInstances
            .CountAsync(x => x.TenantId == tenantId && x.CreatedAt.Year == year, ct);

    public async Task<AddProcedureInstanceOutcome> AddWithUniqueReferenceAsync(ProcedureInstance instance, int year, CancellationToken ct)
    {
        await db.ProcedureInstances.AddAsync(instance, ct);

        for (var attempt = 0; attempt < MaxReferenceRetries; attempt++)
        {
            instance.ReferenceNumber = $"TRM-{year}-{await NextSeqAsync(instance.TenantId, year, ct):D6}";
            try
            {
                await db.SaveChangesAsync(ct);
                return AddProcedureInstanceOutcome.Created;
            }
            catch (DbUpdateException ex) when (IsReferenceUniqueViolation(ex))
            {
                // Colisión de reference_number bajo concurrencia: regenera el siguiente seq y reintenta.
                // EF deja la entidad marcada como Added tras el fallo, así que el siguiente SaveChanges
                // reintenta el mismo insert con la nueva referencia.
            }
            catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
            {
                // tenant_id / created_by_user_id / procedure_type_id inexistente: no tiene sentido
                // reintentar. Se traduce a 422 en el handler/endpoint (antes burbujeaba como 500).
                return AddProcedureInstanceOutcome.ReferencedEntityMissing;
            }
        }

        return AddProcedureInstanceOutcome.ReferenceConflict;
    }

    /// <summary>MAX(seq) + 1 por (tenant, year) parseando el sufijo D6 de las referencias existentes.</summary>
    private async Task<int> NextSeqAsync(Guid tenantId, int year, CancellationToken ct)
    {
        var prefix = $"TRM-{year}-";
        var references = await db.ProcedureInstances
            .Where(x => x.TenantId == tenantId && x.ReferenceNumber.StartsWith(prefix))
            .Select(x => x.ReferenceNumber)
            .ToListAsync(ct);

        var max = 0;
        foreach (var reference in references)
        {
            var suffix = reference[prefix.Length..];
            if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var seq) && seq > max)
                max = seq;
        }

        return max + 1;
    }

    private static bool IsReferenceUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation
        && pg.ConstraintName == ReferenceUniqueConstraint;

    private static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.ForeignKeyViolation;

    public async Task<Guid?> GetFormFieldIdByKeyAsync(Guid procedureTypeId, string fieldKey, CancellationToken ct)
    {
        var matches = await db.FormFields
            .Where(f => f.FieldKey == fieldKey
                && f.ProcedureSection!.ProcedureStep!.ProcedureTypeId == procedureTypeId)
            .Select(f => f.Id)
            .Take(1)
            .ToListAsync(ct);

        return matches.Count > 0 ? matches[0] : null;
    }

    public async Task AddAsync(ProcedureInstance instance, CancellationToken ct)
    {
        await db.ProcedureInstances.AddAsync(instance, ct);
    }

    public void Add<TEntity>(TEntity entity) where TEntity : class =>
        db.Add(entity);

    public Task UpdateAsync(ProcedureInstance instance, CancellationToken ct)
    {
        db.ProcedureInstances.Update(instance);
        return Task.CompletedTask;
    }

    public void RemoveAttachment(ProcedureInstanceAttachment attachment) =>
        db.Set<ProcedureInstanceAttachment>().Remove(attachment);

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}

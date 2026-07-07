using System.Globalization;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Flit.Tramites.Domain.Tramites.Estados;

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

    public async Task<IReadOnlyList<IdentityValidationAuditEvent>> ListIdentityAuditByValidationAsync(
        Guid validationId, CancellationToken ct) =>
        await db.IdentityValidationAudits
            .AsNoTracking()
            .Where(x => x.ValidationId == validationId)
            .OrderBy(x => x.OccurredAt)
            .ToListAsync(ct);

    public Task<ProcedureInstance?> GetByIdWithAttachmentsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithActorsAndAttachmentsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .AsSplitQuery()
            .Include(x => x.Actors)
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
                && i.Status == TramiteEstado.Borrador
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

    public async Task<IReadOnlyList<ProcedureInstance>> ListWithSummaryGraphAsync(Guid? tenantId, int limit, CancellationToken ct)
    {
        // tenantId null = TODOS los tenants (SuperAdmin ve todo, #1). El enforcement de pertenencia
        // se hace antes (middleware): aquí null solo llega para un caller multi-tenant autorizado.
        var query = db.ProcedureInstances
            .AsSplitQuery()
            .Include(x => x.FieldValues)
            .Include(x => x.Actors)
            .Include(x => x.Attachments)
            .Include(x => x.Commercial)
            .Include(x => x.PreflightSnapshots)
            .Include(x => x.BiometricValidations)
            .Include(x => x.Signatures)
            .Where(x => x.DeletedAt == null);

        if (tenantId is { } tid)
            query = query.Where(x => x.TenantId == tid);

        return await query
            // HU #10536 — los prioritarios se listan con primacía; dentro de cada grupo, por recencia.
            .OrderByDescending(x => x.Prioritario)
            .ThenByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetTenantNamesAsync(
        IReadOnlyCollection<Guid> tenantIds, CancellationToken ct)
    {
        if (tenantIds.Count == 0)
            return new Dictionary<Guid, string>();

        var distinct = tenantIds.Distinct().ToList();
        var rows = await db.Tenants
            .Where(t => distinct.Contains(t.Id))
            .Select(t => new { t.Id, t.LegalName })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Id, r => r.LegalName);
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
        // Filtro grueso en SQL. `valid_until` es la fuente de verdad del vencimiento (editable en BD); cuando
        // está, se filtra por él (> now). Si falta (registros viejos), se cae al corte por validated_at con un
        // día de margen. El corte fino lo aplica BiometricRules.EsAprobadaVigente (misma prioridad valid_until).
        var cutoff = now.AddDays(-(BiometricRules.VigenciaDias + 1));
        var candidates = await db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId
                && v.Status == BiometricEstados.Aprobado
                && v.DocumentType == tipoDoc
                && v.DocumentNumber == documento
                && ((v.ValidUntil != null && v.ValidUntil > now)
                    || (v.ValidUntil == null && v.ValidatedAt != null && v.ValidatedAt >= cutoff))
                && v.ProcedureInstance != null
                && v.ProcedureInstance.DeletedAt == null)
            .OrderByDescending(v => v.ValidatedAt)
            .Take(10)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(v => BiometricRules.EsAprobadaVigente(v, now));
    }

    public async Task<IReadOnlySet<string>> ListVigenteApprovedIdentityKeysAsync(
        IReadOnlyCollection<Guid> tenantIds, DateTimeOffset now, CancellationToken ct = default)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (tenantIds.Count == 0)
            return keys;

        // Mismo filtro grueso que FindVigenteApproved… (corte por timestamp con un día de margen); el corte
        // fino por día calendario se aplica en memoria con EsAprobadaVigente. Una sola consulta para todos
        // los tenants del listado (WHERE tenant_id IN (...)) → sin N+1.
        var cutoff = now.AddDays(-(BiometricRules.VigenciaDias + 1));
        var candidates = await db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Where(v => tenantIds.Contains(v.TenantId)
                && v.Status == BiometricEstados.Aprobado
                && v.DocumentType != null
                && v.DocumentNumber != null
                && ((v.ValidUntil != null && v.ValidUntil > now)
                    || (v.ValidUntil == null && v.ValidatedAt != null && v.ValidatedAt >= cutoff))
                && v.ProcedureInstance != null
                && v.ProcedureInstance.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var v in candidates)
            if (BiometricRules.EsAprobadaVigente(v, now))
                keys.Add(BiometricRules.IdentidadKey(v.TenantId, v.DocumentType, v.DocumentNumber));

        return keys;
    }

    public async Task<IReadOnlyList<ProcedureInstanceBiometricValidation>> ListBiometricValidationsByTenantAsync(
        Guid tenantId,
        int skip,
        int take,
        BiometricValidationListFilter? filter,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var query = BaseTenantBiometricQuery(tenantId);
        query = ApplyBiometricValidationFilters(query, filter, now);

        return await query
            .OrderByDescending(v => v.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountBiometricValidationsByEstadoAsync(
        Guid tenantId,
        BiometricValidationListFilter? filter,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var query = ApplyBiometricValidationFilters(BaseTenantBiometricQuery(tenantId), filter, now);

        var rows = await query
            .GroupBy(v => v.Status)
            .Select(g => new { Estado = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.Estado, x => x.Count);
    }

    private IQueryable<ProcedureInstanceBiometricValidation> BaseTenantBiometricQuery(Guid tenantId) =>
        db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Include(v => v.ProcedureInstance)
            .Where(v => v.TenantId == tenantId
                && v.ProcedureInstance != null
                && v.ProcedureInstance.DeletedAt == null);

    /// <summary>Carácter de escape para los patrones LIKE/ILIKE (saneo de búsqueda).</summary>
    private const string LikeEscapeChar = "\\";

    /// <summary>
    /// Escapa los comodines LIKE (<c>\</c>, <c>%</c>, <c>_</c>) de un término de búsqueda para
    /// que se traten como literales y no como patrón. El backslash se escapa primero.
    /// </summary>
    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static IQueryable<ProcedureInstanceBiometricValidation> ApplyBiometricValidationFilters(
        IQueryable<ProcedureInstanceBiometricValidation> query,
        BiometricValidationListFilter? filter,
        DateTimeOffset now)
    {
        if (filter is null || !filter.HasActiveFilters)
            return query;

        if (!string.IsNullOrWhiteSpace(filter.ReferenceNumber))
        {
            var term = EscapeLike(filter.ReferenceNumber.Trim());
            query = query.Where(v => v.ProcedureInstance != null
                && EF.Functions.ILike(v.ProcedureInstance.ReferenceNumber, $"%{term}%", LikeEscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(filter.Modalidad))
        {
            var term = EscapeLike(filter.Modalidad.Trim());
            query = query.Where(v => v.ProcedureInstance != null
                && EF.Functions.ILike(v.ProcedureInstance.ModalidadEntrada, $"%{term}%", LikeEscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            var term = EscapeLike(filter.Name.Trim().ToLower());
            query = query.Where(v => EF.Functions.ILike(v.Name.ToLower(), $"%{term}%", LikeEscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(filter.PartyRole))
        {
            var parte = filter.PartyRole.Trim().ToLower();
            query = query.Where(v => v.PartyRole != null && v.PartyRole.ToLower() == parte);
        }

        if (!string.IsNullOrWhiteSpace(filter.DocumentType))
        {
            var tipoDoc = filter.DocumentType.Trim().ToLower();
            query = query.Where(v => v.DocumentType.ToLower() == tipoDoc);
        }

        if (!string.IsNullOrWhiteSpace(filter.DocumentNumber))
        {
            var term = EscapeLike(filter.DocumentNumber.Trim());
            query = query.Where(v => EF.Functions.ILike(v.DocumentNumber, $"%{term}%", LikeEscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var estado = filter.Status.Trim().ToLower();
            if (estado == BiometricEstados.Expirado)
            {
                // AC3: expirado incluye estado persistido + flag expired calculado (no aprobada y vencida).
                query = query.Where(v =>
                    v.Status == BiometricEstados.Expirado
                    || (v.Status != BiometricEstados.Aprobado && v.ExpiresAt < now));
            }
            else
            {
                query = query.Where(v => v.Status.ToLower() == estado);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Provider))
        {
            var provider = filter.Provider.Trim().ToLower();
            query = query.Where(v => v.Provider.ToLower() == provider);
        }

        if (filter.ScoreMin is { } scoreMin)
            query = query.Where(v => v.Score != null && v.Score >= scoreMin);

        if (filter.ScoreMax is { } scoreMax)
            query = query.Where(v => v.Score != null && v.Score <= scoreMax);

        if (filter.CreatedFrom is { } createdFrom)
            query = query.Where(v => v.CreatedAt >= createdFrom);

        if (filter.CreatedTo is { } createdTo)
            query = query.Where(v => v.CreatedAt <= createdTo);

        // Vigencia (HU #10350): la identidad APROBADA vence a los VigenciaDias días de validado_at. Para
        // que el filtro sea traducible a SQL se DESPLAZA la constante en vez de sumar a la columna:
        //   expira = validado_at + VigenciaDias  ⇒  (expira ⋈ x)  ⟺  (validado_at ⋈ x - VigenciaDias).
        if (!string.IsNullOrWhiteSpace(filter.VigenciaEstado))
        {
            // validado_at > corteVigente  ⇒ aún vigente; <= ⇒ ya vencida.
            var corteVigente = now.AddDays(-BiometricRules.VigenciaDias);
            // validado_at <= cortePorVencer ⇒ le quedan ≤ VigenciaPorVencerDias días de vigencia.
            var cortePorVencer = now.AddDays(BiometricRules.VigenciaPorVencerDias - BiometricRules.VigenciaDias);
            query = filter.VigenciaEstado.Trim().ToLowerInvariant() switch
            {
                BiometricVigenciaEstados.Vigente => query.Where(v =>
                    v.Status == BiometricEstados.Aprobado && v.ValidatedAt != null && v.ValidatedAt > corteVigente),
                BiometricVigenciaEstados.PorVencer => query.Where(v =>
                    v.Status == BiometricEstados.Aprobado && v.ValidatedAt != null
                    && v.ValidatedAt > corteVigente && v.ValidatedAt <= cortePorVencer),
                BiometricVigenciaEstados.Vencida => query.Where(v =>
                    v.Status == BiometricEstados.Aprobado && v.ValidatedAt != null && v.ValidatedAt <= corteVigente),
                _ => query,
            };
        }

        // Rango por fecha de fin de vigencia (validado_at + VigenciaDias) → se desplaza el límite a validado_at.
        if (filter.ExpiraDesde is { } expiraDesde)
            query = query.Where(v => v.ValidatedAt != null
                && v.ValidatedAt >= expiraDesde.AddDays(-BiometricRules.VigenciaDias));

        if (filter.ExpiraHasta is { } expiraHasta)
            query = query.Where(v => v.ValidatedAt != null
                && v.ValidatedAt <= expiraHasta.AddDays(-BiometricRules.VigenciaDias));

        // "Vence en ≤ N días": aprobadas AÚN VIGENTES (validado_at > now - VigenciaDias) cuya expiración
        // (validado_at + VigenciaDias) cae dentro de N días ⟺ validado_at <= now + N - VigenciaDias.
        if (filter.VenceEnDias is { } venceEnDias)
        {
            var corteVigente = now.AddDays(-BiometricRules.VigenciaDias);
            var corteVenceEn = now.AddDays(venceEnDias - BiometricRules.VigenciaDias);
            query = query.Where(v =>
                v.Status == BiometricEstados.Aprobado && v.ValidatedAt != null
                && v.ValidatedAt > corteVigente && v.ValidatedAt <= corteVenceEn);
        }

        // NOTA: el filtro `motivoRechazo` NO se aplica aquí. Detalle/ProviderPayload son columnas `jsonb`
        // y PostgreSQL no soporta el operador ILIKE sobre jsonb (falla con 42883 like_escape(jsonb,...)).
        // Se resuelve en memoria en el handler sobre el motivo SANITIZADO (ExtractMotivoRechazo), que además
        // es el texto que ve el gestor (para Kyverum el motivo es derivado, no literal del payload).
        return query;
    }

    public Task<ProcedureInstanceBiometricValidation?> GetBiometricByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        db.ProcedureInstanceBiometricValidations
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

    public Task<ProcedureInstanceBiometricValidation?> GetBiometricByIdAsync(Guid id, CancellationToken ct) =>
        db.ProcedureInstanceBiometricValidations
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    // UPDATE atómico e idempotente del conteo de intentos Kyverum. La guarda `last_attempt_at <> @key`
    // (más el row-lock del UPDATE) garantiza que dos entregas paralelas del MISMO intento cuenten una sola vez.
    public async Task<bool> TryCountKyverumAttemptAsync(
        Guid validationId, string attemptKey, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await db.ProcedureInstanceBiometricValidations
            .Where(x => x.Id == validationId
                        && x.Status == BiometricEstados.EnProceso
                        && (x.LastAttemptAt == null || x.LastAttemptAt != attemptKey))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                .SetProperty(x => x.LastAttemptAt, attemptKey)
                .SetProperty(x => x.ReconcilePollCount, 0)
                .SetProperty(x => x.UpdatedAt, now), ct);
        return affected > 0;
    }

    public Task ReloadBiometricAsync(ProcedureInstanceBiometricValidation validation, CancellationToken ct) =>
        db.Entry(validation).ReloadAsync(ct);

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

    // HU #10431 — guarda FK para changed_by en status_history: evita violar la FK a identity.users
    // cuando el sujeto de la radicación no existe (proceso automático o claim sub inválido).
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);

    // N 03 (RNF01) — commit con guarda de concurrencia optimista: row_version es concurrency
    // token (lo incrementa el trigger tr_procedure_instances_row_version); si otro proceso
    // transicionó la instancia entre carga y commit, EF lanza DbUpdateConcurrencyException y
    // aquí se traduce a false SIN efectos parciales (Application no referencia EF).
    public async Task<bool> SaveChangesWithConcurrencyGuardAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task<(IReadOnlyList<ProcedureInstanceStatusHistoryEntry> Items, int Total)?> GetStatusHistoryPageAsync(
        Guid id, Guid tenantId, int skip, int take, CancellationToken ct)
    {
        var exists = await db.ProcedureInstances.AsNoTracking()
            .AnyAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);
        if (!exists)
            return null;

        var query = db.ProcedureInstanceStatusHistories.AsNoTracking()
            .Where(h => h.ProcedureInstanceId == id && h.TenantId == tenantId);

        var total = await query.CountAsync(ct);

        // Left join a identity.users vía subconsulta (se traduce a LEFT JOIN LATERAL y funciona
        // igual con el provider InMemory de los tests). Desempate por Id para orden determinista
        // cuando dos transiciones comparten changed_at (p. ej. borrador→preparado→entregado del submit).
        var items = await query
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .Skip(skip)
            .Take(take)
            .Select(h => new ProcedureInstanceStatusHistoryEntry(
                h.Id,
                h.FromStatus,
                h.ToStatus,
                h.ChangedAt,
                h.ChangedBy,
                db.Users.Where(u => u.Id == h.ChangedBy).Select(u => u.DisplayName).FirstOrDefault(),
                h.Reason))
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
}

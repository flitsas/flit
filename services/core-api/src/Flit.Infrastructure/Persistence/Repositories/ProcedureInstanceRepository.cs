using System.Globalization;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class ProcedureInstanceRepository(FlitDbContext db) : IProcedureInstanceRepository
{
    private const string ReferenceUniqueConstraint = "uq_procedure_instances_tenant_reference";
    private const int MaxReferenceRetries = 5;
    public Task<ProcedureInstance?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    // HU #10538 (R3) — invariante "un VIN → una matrícula": busca otras matrículas iniciales del tenant
    // con el mismo VIN para ofrecer la ruta de traspaso. El VIN se guarda en field_values (no columna),
    // así que se compara la variante almacenada trim+upper contra el VIN ya normalizado por el caller
    // (VinNormalizer). La secretaría (nombre del OT) y la fecha del registro previo se resuelven con
    // subconsultas de proyección — mismo patrón que GetStatusHistoryPageAsync con identity.users.
    public async Task<IReadOnlyList<VinTramiteExistente>> FindTramitesByVinAsync(
        Guid tenantId, string vinNormalizado, Guid excludeInstanceId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(vinNormalizado))
            return [];

        var rows = await db.ProcedureInstances
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId
                && i.DeletedAt == null
                && i.Id != excludeInstanceId
                && i.ModalidadEntrada == TramiteModalidadEntradaCodes.MatriculaInicial
                && i.FieldValues.Any(f => f.FieldKey == "vin"
                    && f.ValueText != null
                    && f.ValueText.Trim().ToUpper() == vinNormalizado))
            .Select(i => new
            {
                i.Id,
                i.Status,
                i.SubsanacionActiva,
                i.CompletedAt,
                i.SubmittedAt,
                i.CreatedAt,
                Placa = i.FieldValues
                    .Where(f => f.FieldKey == "plate")
                    .Select(f => f.ValueText)
                    .FirstOrDefault(),
                Secretaria = i.TransitOfficeId == null
                    ? null
                    : db.TransitOffices
                        .Where(o => o.Id == i.TransitOfficeId)
                        .Select(o => o.Name)
                        .FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Orden por recencia (fecha del registro previo) desc en memoria: el primer bloqueante define el
        // mensaje en VinPolicyEvaluator. El conjunto es pequeño (matrículas del mismo VIN en un tenant).
        return rows
            .OrderByDescending(r => r.CompletedAt ?? r.SubmittedAt ?? r.CreatedAt)
            .Select(r => new VinTramiteExistente(
                r.Id,
                r.Status,
                Paso: 0,
                Placa: r.Placa,
                Vin: vinNormalizado,
                Secretaria: r.Secretaria,
                FechaRegistro: r.CompletedAt ?? r.SubmittedAt ?? r.CreatedAt,
                SubsanacionActiva: r.SubsanacionActiva))
            .ToList();
    }

    // HU #10876 (CF-01) — bloqueo de duplicidad EN PROCESO para la familia Traspaso: busca otros
    // trámites de traspaso del tenant con la misma placa. Simétrico a FindTramitesByVinAsync (misma
    // convención de comparación trim+upper contra field_values, mismo patrón de subconsulta), pero
    // sobre ModalidadEntrada == Traspaso y FieldKey == "plate". El VIN (si existe) se proyecta solo
    // como dato informativo del registro previo, no participa en la comparación.
    public async Task<IReadOnlyList<PlacaTramiteExistente>> FindTramitesByPlacaAsync(
        Guid tenantId, string placaNormalizada, Guid excludeInstanceId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(placaNormalizada))
            return [];

        var rows = await db.ProcedureInstances
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId
                && i.DeletedAt == null
                && i.Id != excludeInstanceId
                && i.ModalidadEntrada == TramiteModalidadEntradaCodes.Traspaso
                && i.FieldValues.Any(f => f.FieldKey == "plate"
                    && f.ValueText != null
                    && f.ValueText.Trim().ToUpper() == placaNormalizada))
            .Select(i => new
            {
                i.Id,
                i.Status,
                i.SubsanacionActiva,
                i.CompletedAt,
                i.SubmittedAt,
                i.CreatedAt,
                Vin = i.FieldValues
                    .Where(f => f.FieldKey == "vin")
                    .Select(f => f.ValueText)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Mismo orden por recencia desc que FindTramitesByVinAsync (determinismo, aunque
        // DuplicateActiveProcedurePolicy solo necesita el PRIMER "en proceso", no el más reciente).
        return rows
            .OrderByDescending(r => r.CompletedAt ?? r.SubmittedAt ?? r.CreatedAt)
            .Select(r => new PlacaTramiteExistente(
                r.Id,
                r.Status,
                Placa: placaNormalizada,
                Vin: r.Vin,
                FechaRegistro: r.CompletedAt ?? r.SubmittedAt ?? r.CreatedAt,
                SubsanacionActiva: r.SubsanacionActiva))
            .ToList();
    }

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

    public Task<ProcedureInstance?> GetByIdWithChecklistGraphAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .AsSplitQuery()
            .Include(x => x.Attachments)
            .Include(x => x.Actors)
            .Include(x => x.FieldValues)
            .Include(x => x.Participants)
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
            // HU #10522 — Participants alimenta TieneTramitador (RF39) en el contexto del checklist:
            // el gate "gestor manda" debe verlo igual que el display (GetByIdWithChecklistGraphAsync).
            .Include(x => x.Participants)
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
            .Include(x => x.StatusHistory)
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

    public async Task<IReadOnlyDictionary<Guid, string>> GetUserDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, string>();

        var distinct = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
            return new Dictionary<Guid, string>();

        var rows = await db.Users
            .Where(u => distinct.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync(ct);

        // Un usuario sin nombre visible no aporta a la columna: se omite y la fila cae al fallback.
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.DisplayName))
            .ToDictionary(r => r.Id, r => r.DisplayName);
    }

    public async Task<IReadOnlyDictionary<string, bool>> ListFirmaBaulVigenciaKeysAsync(
        IReadOnlyCollection<Guid> tenantIds, DateOnly hoy, CancellationToken ct)
    {
        if (tenantIds.Count == 0)
            return new Dictionary<string, bool>();

        var distinct = tenantIds.Distinct().ToList();
        var rows = await db.SignatureVault
            .AsNoTracking()
            .Where(v => distinct.Contains(v.TenantId))
            .Select(v => new { v.TenantId, v.DocumentType, v.DocumentNumber, v.Estado, v.VigenciaDesde, v.VigenciaHasta })
            .ToListAsync(ct);

        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            // Vigente = activa y hoy dentro de [desde, hasta] (ADR-0025): una revocada o fuera de rango
            // existe pero ya no sirve, y esa diferencia es justo la que la columna necesita mostrar.
            var vigente = string.Equals(r.Estado, SignatureVaultEstadoActiva, StringComparison.OrdinalIgnoreCase)
                && r.VigenciaDesde <= hoy
                && r.VigenciaHasta >= hoy;

            var key = BiometricRules.IdentidadKey(r.TenantId, r.DocumentType, r.DocumentNumber);
            // Con varias firmas de la misma persona, una vigente manda sobre las caducadas.
            result[key] = result.TryGetValue(key, out var previa) ? previa || vigente : vigente;
        }

        return result;
    }

    /// <summary>Estado "activa" del baúl (ADR-0025): las revocadas no cuentan como vigentes.</summary>
    private const string SignatureVaultEstadoActiva = "activa";

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
                // Migración V1→V2: una identidad traída de V1 vale SOLO para su trámite (ver
                // BiometricProviders.MigracionV1). Sin esta exclusión apalancaría trámites nativos de V2.
                && v.Provider != BiometricProviders.MigracionV1
                && ((v.ValidUntil != null && v.ValidUntil > now)
                    || (v.ValidUntil == null && v.ValidatedAt != null && v.ValidatedAt >= cutoff))
                // HU #10867 — incluir prevalidaciones standalone (sin trámite) y las ligadas a instancias no eliminadas.
                && (v.ProcedureInstanceId == null
                    || (v.ProcedureInstance != null && v.ProcedureInstance.DeletedAt == null)))
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
                // Migración V1→V2: una identidad traída de V1 vale SOLO para su trámite (ver
                // BiometricProviders.MigracionV1). Sin esta exclusión apalancaría trámites nativos de V2.
                && v.Provider != BiometricProviders.MigracionV1
                && ((v.ValidUntil != null && v.ValidUntil > now)
                    || (v.ValidUntil == null && v.ValidatedAt != null && v.ValidatedAt >= cutoff))
                // HU #10867 — incluir prevalidaciones standalone (sin trámite) y las ligadas a instancias no eliminadas.
                && (v.ProcedureInstanceId == null
                    || (v.ProcedureInstance != null && v.ProcedureInstance.DeletedAt == null)))
            .ToListAsync(ct);

        foreach (var v in candidates)
            if (BiometricRules.EsAprobadaVigente(v, now))
                keys.Add(BiometricRules.IdentidadKey(v.TenantId, v.DocumentType, v.DocumentNumber));

        return keys;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<LinkedProcedureSummary>>> ListLinkedProceduresByIdentityDocumentsAsync(
        Guid tenantId,
        IReadOnlyCollection<(string DocumentType, string DocumentNumber)> documents,
        CancellationToken ct = default)
    {
        if (documents.Count == 0)
            return new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>();

        var requestedKeys = documents
            .Select(d => BiometricRules.IdentidadKey(tenantId, d.DocumentType, d.DocumentNumber))
            .ToHashSet(StringComparer.Ordinal);

        var documentNumbers = documents
            .Select(d => d.DocumentNumber.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => n.ToUpperInvariant())
            .ToList();

        if (documentNumbers.Count == 0)
            return new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>();

        // 1) Trámites con validación biométrica de esa identidad (histórico Feature #11066).
        var fromBio = await db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId
                && v.ProcedureInstanceId != null
                && v.ProcedureInstance != null
                && v.ProcedureInstance.DeletedAt == null
                && documentNumbers.Contains(v.DocumentNumber.ToUpper()))
            .Select(v => new
            {
                v.DocumentType,
                v.DocumentNumber,
                InstanceId = v.ProcedureInstanceId!.Value,
                v.ProcedureInstance!.ReferenceNumber,
                v.ProcedureInstance.Status,
                Modalidad = v.ProcedureInstance.ModalidadEntrada,
            })
            .ToListAsync(ct);

        // 2) HU #11069 — también trámites donde la persona es actor (mismo tipo+documento).
        // Una identidad aprobada (p. ej. prevalidación) se reutiliza en varios trámites sin crear
        // otra fila biométrica por instancia; sin este join solo aparecería 1 trámite.
        var fromActors = await db.ProcedureInstanceActors
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId
                && documentNumbers.Contains(a.DocumentNumber.ToUpper())
                && a.ProcedureInstance != null
                && a.ProcedureInstance.DeletedAt == null)
            .Select(a => new
            {
                a.DocumentType,
                a.DocumentNumber,
                InstanceId = a.ProcedureInstanceId,
                a.ProcedureInstance!.ReferenceNumber,
                a.ProcedureInstance.Status,
                Modalidad = a.ProcedureInstance.ModalidadEntrada,
            })
            .ToListAsync(ct);

        return fromBio.Concat(fromActors)
            .Where(r => requestedKeys.Contains(BiometricRules.IdentidadKey(tenantId, r.DocumentType, r.DocumentNumber)))
            .GroupBy(r => BiometricRules.IdentidadKey(tenantId, r.DocumentType, r.DocumentNumber))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<LinkedProcedureSummary>)g
                    .DistinctBy(r => r.InstanceId)
                    .OrderBy(r => r.ReferenceNumber, StringComparer.OrdinalIgnoreCase)
                    .Select(r => new LinkedProcedureSummary(r.InstanceId, r.ReferenceNumber, r.Status, r.Modalidad))
                    .ToList());
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
            // HU #10867 — incluir prevalidaciones standalone (ProcedureInstanceId IS NULL) + las ligadas a instancias no eliminadas.
            .Where(v => v.TenantId == tenantId
                && (v.ProcedureInstanceId == null
                    || (v.ProcedureInstance != null && v.ProcedureInstance.DeletedAt == null)));

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

        // HU #10867 — filtro standalone: true = solo prevalidaciones sin trámite; false = solo ligadas; null = todas.
        if (filter.Standalone is { } standalone)
        {
            query = standalone
                ? query.Where(v => v.ProcedureInstanceId == null)
                : query.Where(v => v.ProcedureInstanceId != null);
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
            .Include(x => x.ProcedureInstance)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    // HU #10943 (CF-03) — TRACKEADA (editar/reenviar la modifica) + Person incluida (ResolveSubject).
    public Task<ProcedureInstanceBiometricValidation?> GetBiometricByIdWithPersonAsync(
        Guid id, Guid tenantId, CancellationToken ct = default) =>
        db.ProcedureInstanceBiometricValidations
            .Include(x => x.Person)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);

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

    public async Task<IReadOnlySet<string>> ListRuntConsultedDocumentKeysAsync(
        Guid id, Guid tenantId, CancellationToken ct)
    {
        // El payload es jsonb: se traen los eventos del tipo y la clave se arma en memoria (mismo
        // motivo que GetLatestSubsanacionMetadataAsync — no se puede parsear jsonb desde LINQ).
        var payloads = await db.ProcedureInstanceEvents.AsNoTracking()
            .Where(e => e.ProcedureInstanceId == id
                && e.TenantId == tenantId
                && e.Tipo == RuntPersonaConsultada.Tipo)
            .Select(e => e.Payload)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return payloads
            .Select(RuntPersonaConsultada.KeyFromPayload)
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);
    }

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

    /// <summary>HU #11029 — ver <see cref="IProcedureInstanceRepository.ResetTracking"/>.</summary>
    public void ResetTracking() => db.ChangeTracker.Clear();

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
                h.Reason)
            {
                Metadata = h.Metadata,
            })
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

    // HU #10872 (AC1) — field values lean para callers que transicionan a subsanación sin cargar el
    // grafo completo del wizard (p. ej. ConsultarEstadoQuipuxHandler).
    public async Task<IReadOnlyList<ProcedureInstanceFieldValue>> GetFieldValuesAsync(
        Guid instanceId, Guid tenantId, CancellationToken ct) =>
        await db.ProcedureInstanceFieldValues.AsNoTracking()
            .Where(f => f.ProcedureInstanceId == instanceId && f.TenantId == tenantId)
            .ToListAsync(ct);

    // Baseline del diff de re-radicación: metadata MÁS RECIENTE que trae fieldSnapshot
    // (activación de subsanación, observación OT, o legado to_status='subsanacion').
    // Metadata es jsonb: NO usar string.Contains en LINQ (EF emite LIKE/`~~` sobre jsonb →
    // Postgres 42883). Se traen candidatos recientes y el filtro "fieldSnapshot" es en memoria.
    public async Task<string?> GetLatestSubsanacionMetadataAsync(
        Guid instanceId, Guid tenantId, CancellationToken ct)
    {
        // Fuente primaria: la columna de la instancia, que es donde escribe la activación de la
        // subsanación desde que dejó de fabricar una transición rechazado → rechazado.
        var baseline = await db.ProcedureInstances.AsNoTracking()
            .Where(i => i.Id == instanceId && i.TenantId == tenantId)
            .Select(i => i.SubsanacionBaseline)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(baseline))
            return baseline;

        // Respaldo: expedientes anteriores a la columna y observaciones de rechazo del OT/Quipux,
        // que siguen trayendo el snapshot en el metadata de su transición real.
        var candidates = await db.ProcedureInstanceStatusHistories.AsNoTracking()
            .Where(h => h.ProcedureInstanceId == instanceId
                && h.TenantId == tenantId
                && h.Metadata != null
                && (h.ToStatus == TramiteEstado.Subsanacion
                    || h.ToStatus == TramiteEstado.Rechazado))
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .Select(h => h.Metadata)
            .Take(30)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return candidates.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m)
            && (m.Contains("fieldSnapshot", StringComparison.Ordinal)
                || SubsanacionObservation.FromJson(m)?.FieldSnapshot is not null));
    }

    // HU #10955 (AC2/AC3/AC5) — lookup de datos de contacto ya conocidos de una persona, a través de
    // TODOS sus trámites (no eliminados) del tenant. Tenant explícito en el WHERE (AC5); el actor de
    // la instancia más reciente por CreatedAt (mismo criterio de "recencia" que FindTramitesByVinAsync).
    public Task<ProcedureInstanceActor?> FindLatestActorContactAsync(
        Guid tenantId, string documentType, string documentNumber, CancellationToken ct) =>
        db.ProcedureInstanceActors.AsNoTracking()
            .Where(a => a.TenantId == tenantId
                && a.DocumentType == documentType
                && a.DocumentNumber == documentNumber
                && a.ProcedureInstance!.DeletedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

    // HU filtrado/ordenamiento server-side del listado — ver IProcedureInstanceRepository. El WHERE y
    // el ORDER BY se resuelven ACÁ, en SQL, sobre columnas propias o denormalizadas; el grafo completo
    // (Include) se carga solo para las filas de la página ya resuelta por el motor (mismo patrón de
    // ListWithSummaryGraphAsync: AsSplitQuery + Include + Where + OrderBy + Take, con Skip agregado).
    public async Task<(IReadOnlyList<ProcedureInstance> Items, int Total)> ListWithSummaryGraphFilteredAsync(
        Guid? tenantId,
        int skip,
        int take,
        ProcedureInstanceListFilter filter,
        ProcedureInstanceSortBy sortBy,
        SortDirection direction,
        CancellationToken ct)
    {
        // AsNoTracking: lectura pura para un listado, sin intención de modificar las entidades cargadas
        // (checklist B6).
        var baseQuery = db.ProcedureInstances.AsNoTracking().Where(x => x.DeletedAt == null);
        if (tenantId is { } tid)
            baseQuery = baseQuery.Where(x => x.TenantId == tid);

        baseQuery = ApplyListFilters(baseQuery, filter);

        var total = await baseQuery.CountAsync(ct);

        var ordered = ApplyListSort(baseQuery, sortBy, direction);

        var items = await ordered
            .AsSplitQuery()
            .Include(x => x.FieldValues)
            .Include(x => x.Actors)
            .Include(x => x.Attachments)
            .Include(x => x.Commercial)
            .Include(x => x.PreflightSnapshots)
            .Include(x => x.BiometricValidations)
            .Include(x => x.Signatures)
            .Include(x => x.StatusHistory)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <summary>
    /// Aplica los filtros de <see cref="ProcedureInstanceListFilter"/>. VIN/placa comparan por IGUALDAD
    /// case-insensitive (<c>ToUpper() == ...</c>, mismo criterio de <see cref="FindTramitesByVinAsync"/>);
    /// vendedor/comprador/gestor por SUBCADENA con <c>ToLower().Contains(...)</c> en vez de
    /// <c>EF.Functions.ILike</c> (el patrón usado en <see cref="ApplyBiometricValidationFilters"/>) a
    /// propósito: <c>Contains</c> es traducible tanto por Npgsql (a <c>LIKE</c>, escapando comodines del
    /// término automáticamente) como por el proveedor InMemory usado en los tests de este repositorio —
    /// <c>EF.Functions.ILike</c> es específico de Npgsql y no se puede ejercitar con InMemory.
    /// </summary>
    private IQueryable<ProcedureInstance> ApplyListFilters(
        IQueryable<ProcedureInstance> query, ProcedureInstanceListFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Vin))
        {
            var vin = filter.Vin.Trim().ToUpperInvariant();
            query = query.Where(x => x.Vin != null && x.Vin.ToUpper() == vin);
        }

        if (!string.IsNullOrWhiteSpace(filter.Placa))
        {
            var placa = filter.Placa.Trim().ToUpperInvariant();
            query = query.Where(x => x.Plate != null && x.Plate.ToUpper() == placa);
        }

        if (!string.IsNullOrWhiteSpace(filter.Vendedor))
        {
            var term = filter.Vendedor.Trim().ToLowerInvariant();
            query = query.Where(x => x.VendedorNombre != null && x.VendedorNombre.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(filter.Comprador))
        {
            var term = filter.Comprador.Trim().ToLowerInvariant();
            query = query.Where(x => x.CompradorNombre != null && x.CompradorNombre.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(filter.Gestor))
        {
            var term = filter.Gestor.Trim().ToLowerInvariant();
            query = query.Where(x => db.Users.Any(u => u.Id == x.CreatedByUserId && u.DisplayName.ToLower().Contains(term)));
        }

        if (filter.Firmado is { } firmadoCompleto)
        {
            // "Completa" = firma de compraventa FIRMADA del comprador y, si aplica (traspaso), también
            // del vendedor (la matrícula inicial no lleva compraventa, así que el vendedor no cuenta).
            // Espejo del cálculo de ListProcedureInstancesHandler.DeriveSignaturePending, pero comparado
            // contra el booleano pedido (positivo = completa) en vez de negado.
            query = query.Where(x =>
                (x.Signatures.Any(s => s.Parte == SignatureRules.ParteComprador
                        && s.DocTipo == SignatureDocTipos.Compraventa && s.Estado == SignatureEstados.Firmada)
                    && (x.ModalidadEntrada != TramiteModalidadEntradaCodes.Traspaso
                        || x.Signatures.Any(s => s.Parte == SignatureRules.ParteVendedor
                            && s.DocTipo == SignatureDocTipos.Compraventa && s.Estado == SignatureEstados.Firmada)))
                == firmadoCompleto);
        }

        if (filter.CreatedFrom is { } createdFrom)
            query = query.Where(x => x.CreatedAt >= createdFrom);
        if (filter.CreatedTo is { } createdTo)
            query = query.Where(x => x.CreatedAt <= createdTo);
        if (filter.UpdatedFrom is { } updatedFrom)
            query = query.Where(x => x.UpdatedAt != null && x.UpdatedAt >= updatedFrom);
        if (filter.UpdatedTo is { } updatedTo)
            query = query.Where(x => x.UpdatedAt != null && x.UpdatedAt <= updatedTo);

        return query;
    }

    /// <summary>
    /// Aplica el <c>ORDER BY</c> ya resuelto contra la lista blanca (<see cref="ProcedureInstanceSortBy"/>).
    /// Todas las ramas desempatan por <c>Id</c> para que la paginación sea DETERMINISTA (sin esto, filas
    /// con el mismo valor de orden —p. ej. muchos VIN nulos— podrían repetirse o saltarse entre páginas).
    /// "Gestor" ordena por el <c>DisplayName</c> resuelto vía subconsulta correlacionada a
    /// <c>identity.users</c> (no se denormaliza — ver justificación en Ddl/47-tramites-campos-busqueda.sql).
    /// </summary>
    private IOrderedQueryable<ProcedureInstance> ApplyListSort(
        IQueryable<ProcedureInstance> query, ProcedureInstanceSortBy sortBy, SortDirection direction)
    {
        var descending = direction == SortDirection.Descending;
        return sortBy switch
        {
            ProcedureInstanceSortBy.Vin => descending
                ? query.OrderByDescending(x => x.Vin).ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.Vin).ThenBy(x => x.Id),
            ProcedureInstanceSortBy.Placa => descending
                ? query.OrderByDescending(x => x.Plate).ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.Plate).ThenBy(x => x.Id),
            ProcedureInstanceSortBy.Comprador => descending
                ? query.OrderByDescending(x => x.CompradorNombre).ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.CompradorNombre).ThenBy(x => x.Id),
            ProcedureInstanceSortBy.UpdatedAt => descending
                ? query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id),
            ProcedureInstanceSortBy.CreatedAt => descending
                ? query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            ProcedureInstanceSortBy.Gestor => descending
                ? query.OrderByDescending(x => db.Users.Where(u => u.Id == x.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenByDescending(x => x.Id)
                : query.OrderBy(x => db.Users.Where(u => u.Id == x.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenBy(x => x.Id),
            // Default: mismo orden histórico de ListWithSummaryGraphAsync (prioritarios primero, luego
            // recencia), con el desempate determinista añadido.
            _ => query.OrderByDescending(x => x.Prioritario).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
        };
    }
}

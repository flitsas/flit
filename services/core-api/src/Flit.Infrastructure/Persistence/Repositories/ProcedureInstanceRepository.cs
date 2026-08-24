using System.Globalization;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
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
            .Include(x => x.ProcedureType)
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
            .Include(x => x.ProcedureType)
            .Include(x => x.FieldValues)
            .Include(x => x.StatusHistory)
            .Include(x => x.Actors)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithActorsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.ProcedureType)
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
            .Include(x => x.ProcedureType)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithChecklistGraphAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.ProcedureType)
            .AsSplitQuery()
            .Include(x => x.Attachments)
            .Include(x => x.Actors)
            .Include(x => x.FieldValues)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithActorsAndAttachmentsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.ProcedureType)
            .AsSplitQuery()
            .Include(x => x.Actors)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithWizardGraphAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            // ADR-0050 — el wizard se conforma con los pasos/secciones del tipo cuando la instancia
            // no tiene snapshot congelado. Solo esta consulta los necesita: el resto se queda con la
            // navegación simple para no encarecerlas.
            .Include(x => x.ProcedureType)
                .ThenInclude(t => t!.Steps)
                    .ThenInclude(st => st.Sections)
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
            .Include(x => x.ProcedureType)
            .Include(x => x.Signatures)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithFurGraphAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.ProcedureType)
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
            .Include(x => x.ProcedureType)
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
            .Include(x => x.ProcedureType)
            .Include(x => x.BiometricValidations)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public Task<ProcedureInstance?> GetByIdWithBiometricsAndActorsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        db.ProcedureInstances
            .Include(x => x.ProcedureType)
            .Include(x => x.BiometricValidations)
            .Include(x => x.Actors)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null, ct);

    public async Task<ProcedureInstanceBiometricValidation?> FindVigenteApprovedByDocumentAsync(
        Guid tenantId, string tipoDoc, string documento, DateTimeOffset now, CancellationToken ct)
    {
        // Filtro grueso en SQL por timestamp (validado_at >= corte), con un día de margen para no
        // descartar candidatos cerca del límite; el corte fino por DÍA calendario se aplica en memoria
        // con BiometricRules.EsAprobadaVigente (semántica "día de aprobación = día 1; vence el día 31").
        // Documento, ventana de vigencia y prevalidaciones standalone (HU #10867) son la MISMA cláusula
        // que RepresentativeIdentityLookup, compuesta desde BiometricDocumentMatchQuery (Bug #11583).
        var candidates = await db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId
                && v.Status == BiometricEstados.Aprobado
                // Migración V1→V2: una identidad traída de V1 vale SOLO para su trámite (ver
                // BiometricProviders.MigracionV1). Sin esta exclusión apalancaría trámites nativos de V2.
                && v.Provider != BiometricProviders.MigracionV1)
            .WhereDocumentoVigenteCandidato(tipoDoc, documento)
            .WhereVentanaVigencia(now)
            .WhereInstanciaVigente()
            .OrderByDescending(v => v.ValidatedAt)
            .Take(10)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(v => BiometricRules.EsAprobadaVigente(v, now));
    }

    public async Task<IReadOnlyList<ProcedureInstanceBiometricValidation>> ListInFlightByDocumentAsync(
        Guid tenantId, string tipoDoc, string documento, CancellationToken ct = default)
    {
        // HU #11265 — candidatos en vuelo para la precedencia de envío. Misma semántica de documento que
        // FindVigenteApprovedByDocumentAsync (Bug #11583: normalizado vía BiometricDocumentMatchQuery) para
        // no volver a divergir con el gate (AC5).
        return await db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId
                && (v.Status == BiometricEstados.PendienteEnvio
                    || v.Status == BiometricEstados.Enviado
                    || v.Status == BiometricEstados.EnProceso))
            .WhereDocumentoVigenteCandidato(tipoDoc, documento)
            .WhereInstanciaVigente()
            .OrderByDescending(v => v.UpdatedAt ?? v.CreatedAt)
            .Take(20)
            .ToListAsync(ct);
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

    public Task<(IReadOnlyList<BiometricPersonGroupProjection> Rows, int TotalPersons)>
        ListBiometricValidationsGroupedByPersonAsync(
            Guid tenantId,
            int skip,
            int take,
            BiometricPersonGroupFilter? filter,
            DateTimeOffset now,
            CancellationToken ct) =>
        // DISTINCT ON es PostgreSQL; InMemory (tests) usa el equivalente GroupBy en memoria.
        db.Database.IsNpgsql()
            ? ListGroupedByPersonNpgsqlAsync(tenantId, skip, take, filter, now, ct)
            : ListGroupedByPersonInMemoryAsync(tenantId, skip, take, filter, now, ct);

    public async Task<IReadOnlyDictionary<string, int>> CountBiometricPersonsByEstadoAsync(
        Guid tenantId,
        BiometricPersonGroupFilter? filter,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            return await CountGroupedByPersonNpgsqlAsync(tenantId, filter, now, ct);

        // InMemory: se reutiliza el mismo agrupador (sin paginar) para no duplicar los filtros.
        var (rows, _) = await ListGroupedByPersonInMemoryAsync(tenantId, 0, int.MaxValue, filter, now, ct);
        return rows
            .GroupBy(r => EstadoEfectivo(r.Status, r.ExpiresAt, now))
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Estado tal como lo ve el gestor: una validación no aprobada con el enlace vencido es "expirada"
    /// aunque en base siga como enviada o en proceso. Misma regla que el filtro por estado (AC3) y que
    /// el flag <c>Expired</c> del DTO; el conteo de KPIs debe hablar el mismo idioma que la fila.
    /// </summary>
    private static string EstadoEfectivo(string status, DateTimeOffset expiresAt, DateTimeOffset now) =>
        status != BiometricEstados.Aprobado && expiresAt < now
            ? BiometricEstados.Expirado
            : status;

    public async Task<IReadOnlyList<ProcedureInstanceBiometricValidation>>
        ListBiometricValidationsForPersonAlertScanAsync(
            Guid tenantId,
            IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)> documents,
            int alertWindowDays,
            DateTimeOffset now,
            CancellationToken ct)
    {
        if (documents.Count == 0)
            return [];

        var since = now.AddDays(-Math.Max(0, alertWindowDays));
        var typeSet = documents.Select(d => d.DocumentTypeNorm).ToHashSet(StringComparer.Ordinal);
        var numberSet = documents.Select(d => d.DocumentNumberNorm).ToHashSet(StringComparer.Ordinal);
        var pairSet = documents
            .Select(d => $"{d.DocumentTypeNorm}|{d.DocumentNumberNorm}")
            .ToHashSet(StringComparer.Ordinal);

        // Traemos candidatos por tipo/número normalizados y refinamos el par exacto en memoria
        // (el conjunto de la página es pequeño, ≤ pageSize).
        var candidates = await BaseTenantBiometricQuery(tenantId)
            .Where(v => typeSet.Contains(v.DocumentType.Trim().ToUpper())
                && numberSet.Contains(v.DocumentNumber.Trim().ToUpper()))
            .ToListAsync(ct);

        static bool IsTerminal(string status) =>
            status is BiometricEstados.Aprobado or BiometricEstados.Rechazado or BiometricEstados.Expirado;

        return candidates
            .Where(v =>
            {
                var key = $"{DocumentCanonicalNormalization.NormalizePart(v.DocumentType)}|{DocumentCanonicalNormalization.NormalizePart(v.DocumentNumber)}";
                if (!pairSet.Contains(key))
                    return false;
                if (!IsTerminal(v.Status))
                    return true;
                var activity = v.UpdatedAt ?? v.CreatedAt;
                return activity >= since;
            })
            .ToList();
    }

    public async Task<(IReadOnlyList<ProcedureInstanceBiometricValidation> Rows, int Total, bool AnyNonTerminal)>
        ListBiometricValidationsByPersonAsync(
            Guid tenantId,
            string documentType,
            string documentNumber,
            int skip,
            int take,
            CancellationToken ct)
    {
        var (tipo, numero) = DocumentCanonicalNormalization.Normalize(documentType, documentNumber);
        if (tipo.Length == 0 || numero.Length == 0)
            return ([], 0, false);

        var query = BaseTenantBiometricQuery(tenantId)
            .Where(v => v.DocumentType.Trim().ToUpper() == tipo
                && v.DocumentNumber.Trim().ToUpper() == numero);

        var total = await query.CountAsync(ct);
        var anyNonTerminal = await query.AnyAsync(
            v => v.Status != BiometricEstados.Aprobado
                && v.Status != BiometricEstados.Rechazado
                && v.Status != BiometricEstados.Expirado,
            ct);
        var rows = await query
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        return (rows, total, anyNonTerminal);
    }

    /// <summary>
    /// CTE compartida del listado agrupado por persona (HU #11270): normaliza el documento, cuenta el
    /// grupo, se queda con la validación más reciente de cada persona (DISTINCT ON) y aplica los filtros
    /// de estado/fecha/vigencia sobre ESA fila. La usan tanto la página de filas como el conteo por
    /// estado de los KPIs, para que ambos hablen exactamente del mismo conjunto (si divergieran, los
    /// contadores no cuadrarían con las filas que el gestor tiene delante).
    /// </summary>
    private const string GroupedByPersonCteSql = """
            WITH base AS (
                SELECT
                    v.id,
                    v.document_type,
                    v.document_number,
                    upper(btrim(v.document_type)) AS document_type_norm,
                    upper(btrim(v.document_number)) AS document_number_norm,
                    v.name,
                    v.status,
                    v.created_at,
                    v.validated_at,
                    v.valid_until,
                    v.expires_at,
                    v.procedure_instance_id,
                    pi.reference_number,
                    pi.modalidad_entrada AS modalidad,
                    v.party_role,
                    v.email,
                    v.provider,
                    v.score,
                    v.capture_url,
                    v.attempts,
                    v.max_attempts
                FROM tramites.procedure_instance_biometric_validations v
                LEFT JOIN tramites.procedure_instances pi
                    ON pi.id = v.procedure_instance_id
                WHERE v.tenant_id = {0}
                  AND v.deleted_at IS NULL
                  AND (v.procedure_instance_id IS NULL OR pi.deleted_at IS NULL)
                  AND ({1}::text IS NULL OR upper(btrim(v.document_type)) = {1})
                  -- Documento por COINCIDENCIA PARCIAL, igual que el listado plano: el gestor teclea
                  -- los últimos dígitos de la cédula, no el número completo (con "=" el filtro parecía
                  -- roto salvo que se acertara el documento entero).
                  AND ({2}::text IS NULL
                       OR upper(btrim(v.document_number)) LIKE '%' || {2} || '%' ESCAPE '\')
                  AND ({3}::text IS NULL OR v.name ILIKE '%' || {3} || '%' ESCAPE '\')
                  AND ({4}::boolean IS NULL
                       OR ({4} = TRUE AND v.procedure_instance_id IS NULL)
                       OR ({4} = FALSE AND v.procedure_instance_id IS NOT NULL))
            ),
            counted AS (
                SELECT document_type_norm, document_number_norm, COUNT(*)::int AS validation_count
                FROM base
                GROUP BY document_type_norm, document_number_norm
            ),
            latest AS (
                SELECT DISTINCT ON (b.document_type_norm, b.document_number_norm)
                    b.*,
                    c.validation_count
                FROM base b
                JOIN counted c
                  ON c.document_type_norm = b.document_type_norm
                 AND c.document_number_norm = b.document_number_norm
                -- Desempate por id: sin él, dos validaciones de la misma persona con idéntico
                -- created_at hacen que DISTINCT ON elija una u otra en cada ejecución, y la página de
                -- filas y el conteo de KPIs (dos sentencias distintas) pueden quedarse con estados
                -- diferentes para la misma persona.
                ORDER BY b.document_type_norm, b.document_number_norm, b.created_at DESC, b.id DESC
            ),
            filtered AS (
                SELECT *
                FROM latest
                -- Filtra por el estado EFECTIVO (el mismo que se pinta y se cuenta): una validación no
                -- aprobada con el enlace vencido es "expirada" aunque en base siga enviada o en proceso.
                -- Con el estado crudo, "Expirado" no encontraba filas que en pantalla decían Expirado.
                WHERE ({5}::text IS NULL
                       OR (CASE
                             WHEN status <> 'aprobado' AND expires_at < {14} THEN 'expirado'
                             ELSE status
                           END) = {5})
                  AND ({6}::timestamptz IS NULL OR created_at >= {6})
                  AND ({7}::timestamptz IS NULL OR created_at <= {7})
                  AND (
                        {8}::text IS NULL
                     OR ({8} = 'vigente' AND status = 'aprobado' AND validated_at IS NOT NULL AND validated_at > {11})
                     OR ({8} = 'por_vencer' AND status = 'aprobado' AND validated_at IS NOT NULL
                         AND validated_at > {11} AND validated_at <= {12})
                     OR ({8} = 'vencida' AND status = 'aprobado' AND validated_at IS NOT NULL AND validated_at <= {11})
                  )
                  AND ({9}::timestamptz IS NULL OR (validated_at IS NOT NULL AND validated_at >= {9}))
                  AND ({10}::timestamptz IS NULL OR (validated_at IS NOT NULL AND validated_at <= {10}))
                  AND (
                        {13}::timestamptz IS NULL
                     OR (status = 'aprobado' AND validated_at IS NOT NULL
                         AND validated_at > {11} AND validated_at <= {13})
                  )
            )
        """;

    /// <summary>Página de personas ordenada por registro. Params propios: {14} skip, {15} take.</summary>
    private const string GroupedByPersonRowsSql = GroupedByPersonCteSql + "\n" + """
            -- Columnas en snake_case SIN alias PascalCase: UseSnakeCaseNamingConvention
            -- mapea CreatedAt→created_at. AS "CreatedAt" hacía que EF buscara created_at
            -- y no lo hallara → InvalidOperationException 500 en by-person.
            SELECT
                id AS latest_validation_id,
                document_type,
                document_number,
                document_type_norm,
                document_number_norm,
                name,
                status,
                created_at,
                validated_at,
                valid_until,
                expires_at,
                procedure_instance_id,
                reference_number,
                modalidad,
                party_role,
                email,
                provider,
                score,
                capture_url,
                attempts,
                max_attempts,
                validation_count,
                COUNT(*) OVER() AS total_persons
            FROM filtered
            ORDER BY created_at DESC
            OFFSET {15} LIMIT {16}
        """;

    /// <summary>
    /// KPIs de la grilla agrupada: una fila por estado con su número de PERSONAS. Agrupa por el estado
    /// EFECTIVO, el mismo que se pinta en la fila: una validación no aprobada con el enlace vencido es
    /// "expirada" aunque en base siga como enviada o en proceso (misma regla que el filtro por estado
    /// y que el flag <c>Expired</c> del DTO). Si aquí se agrupara por el estado crudo, el contador diría
    /// "En proceso" de una fila que el gestor ve como "Expirado".
    /// </summary>
    private const string GroupedByPersonCountsSql = GroupedByPersonCteSql + "\n" + """
            SELECT
                CASE
                    WHEN status <> 'aprobado' AND expires_at < {14} THEN 'expirado'
                    ELSE status
                END AS status,
                COUNT(*)::int AS person_count
            FROM filtered
            GROUP BY 1
        """;

    /// <summary>
    /// Parámetros {0}..{14} compartidos por ambas colas de <see cref="GroupedByPersonCteSql"/>
    /// ({14} = <c>now</c>, que solo usa la cola de conteo). La página añade {15} skip y {16} take.
    /// </summary>
    private static object[] BuildGroupedByPersonParams(
        Guid tenantId,
        BiometricPersonGroupFilter? filter,
        DateTimeOffset now)
    {
        var name = filter?.Name?.Trim();
        var docType = string.IsNullOrWhiteSpace(filter?.DocumentType)
            ? null
            : DocumentCanonicalNormalization.NormalizePart(filter!.DocumentType);
        // Va dentro de un LIKE '%…%': se escapan los comodines para tratarlos como literales.
        var docNumber = string.IsNullOrWhiteSpace(filter?.DocumentNumber)
            ? null
            : EscapeLike(DocumentCanonicalNormalization.NormalizePart(filter!.DocumentNumber));
        var status = string.IsNullOrWhiteSpace(filter?.Status) ? null : filter!.Status!.Trim().ToLowerInvariant();
        var vigencia = string.IsNullOrWhiteSpace(filter?.VigenciaEstado)
            ? null
            : filter!.VigenciaEstado!.Trim().ToLowerInvariant();
        var createdFrom = filter?.CreatedFrom;
        var createdTo = filter?.CreatedTo;
        var expiraDesde = filter?.ExpiraDesde;
        var expiraHasta = filter?.ExpiraHasta;
        var venceEnDias = filter?.VenceEnDias;
        var standalone = filter?.Standalone;

        var corteVigente = now.AddDays(-BiometricRules.VigenciaDias);
        var cortePorVencer = now.AddDays(-(BiometricRules.VigenciaDias - BiometricRules.VigenciaPorVencerDias));
        var corteVenceEn = venceEnDias is int n
            ? now.AddDays(n - BiometricRules.VigenciaDias)
            : (DateTimeOffset?)null;


        // ExpiraDesde/Hasta en el listado plano desplazan por VigenciaDias sobre validated_at;
        // aquí aplicamos el mismo desplazamiento para no divergir el filtro de vigencia por fecha.
        DateTimeOffset? expiraDesdeShifted = expiraDesde?.AddDays(-BiometricRules.VigenciaDias);
        DateTimeOffset? expiraHastaShifted = expiraHasta?.AddDays(-BiometricRules.VigenciaDias);

        string? nameEscaped = null;
        if (!string.IsNullOrEmpty(name))
            nameEscaped = EscapeLike(name);

        // SqlQueryRaw no acepta nulls tipados en params object[] (CS8604): DBNull → NULL SQL.
        object Db(object? v) => v ?? DBNull.Value;

        return
        [
            Db(tenantId),
            Db(docType),
            Db(docNumber),
            Db(nameEscaped),
            Db(standalone),
            Db(status),
            Db(createdFrom),
            Db(createdTo),
            Db(vigencia),
            Db(expiraDesdeShifted),
            Db(expiraHastaShifted),
            Db(corteVigente),
            Db(cortePorVencer),
            Db(corteVenceEn),
            Db(now),
        ];
    }

    private async Task<(IReadOnlyList<BiometricPersonGroupProjection> Rows, int TotalPersons)>
        ListGroupedByPersonNpgsqlAsync(
            Guid tenantId,
            int skip,
            int take,
            BiometricPersonGroupFilter? filter,
            DateTimeOffset now,
            CancellationToken ct)
    {
        var shared = BuildGroupedByPersonParams(tenantId, filter, now);
        var args = new object[shared.Length + 2];
        shared.CopyTo(args, 0);
        args[^2] = skip;
        args[^1] = take;

        var rows = await db.Database
            .SqlQueryRaw<BiometricPersonGroupSqlRow>(GroupedByPersonRowsSql, args)
            .ToListAsync(ct);

        var total = rows.Count > 0 ? rows[0].TotalPersons : 0;
        var projections = rows.Select(r => r.ToProjection()).ToList();
        return (projections, total);
    }

    private async Task<IReadOnlyDictionary<string, int>> CountGroupedByPersonNpgsqlAsync(
        Guid tenantId,
        BiometricPersonGroupFilter? filter,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<BiometricPersonStatusCountSqlRow>(
                GroupedByPersonCountsSql,
                BuildGroupedByPersonParams(tenantId, filter, now))
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Status, r => r.PersonCount);
    }

    private async Task<(IReadOnlyList<BiometricPersonGroupProjection> Rows, int TotalPersons)>
        ListGroupedByPersonInMemoryAsync(
            Guid tenantId,
            int skip,
            int take,
            BiometricPersonGroupFilter? filter,
            DateTimeOffset now,
            CancellationToken ct)
    {
        var all = await BaseTenantBiometricQuery(tenantId).ToListAsync(ct);

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.DocumentType))
            {
                var tipo = DocumentCanonicalNormalization.NormalizePart(filter.DocumentType);
                all = all.Where(v => DocumentCanonicalNormalization.NormalizePart(v.DocumentType) == tipo).ToList();
            }

            if (!string.IsNullOrWhiteSpace(filter.DocumentNumber))
            {
                // Coincidencia parcial, igual que el LIKE de la consulta Npgsql.
                var numero = DocumentCanonicalNormalization.NormalizePart(filter.DocumentNumber);
                all = all
                    .Where(v => DocumentCanonicalNormalization.NormalizePart(v.DocumentNumber).Contains(numero, StringComparison.Ordinal))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(filter.Name))
            {
                var term = filter.Name.Trim();
                all = all.Where(v => v.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (filter.Standalone is { } standalone)
            {
                all = standalone
                    ? all.Where(v => v.ProcedureInstanceId is null).ToList()
                    : all.Where(v => v.ProcedureInstanceId is not null).ToList();
            }
        }

        var groups = all
            .GroupBy(v => (
                DocumentCanonicalNormalization.NormalizePart(v.DocumentType),
                DocumentCanonicalNormalization.NormalizePart(v.DocumentNumber)))
            .Select(g =>
            {
                // Mismo desempate que el DISTINCT ON de Postgres (created_at, luego id).
                var latest = g.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).First();
                return new BiometricPersonGroupProjection
                {
                    LatestValidationId = latest.Id,
                    DocumentType = latest.DocumentType,
                    DocumentNumber = latest.DocumentNumber,
                    DocumentTypeNorm = g.Key.Item1,
                    DocumentNumberNorm = g.Key.Item2,
                    Name = latest.Name,
                    Status = latest.Status,
                    CreatedAt = latest.CreatedAt,
                    ValidatedAt = latest.ValidatedAt,
                    ValidUntil = latest.ValidUntil,
                    ExpiresAt = latest.ExpiresAt,
                    ProcedureInstanceId = latest.ProcedureInstanceId,
                    ReferenceNumber = latest.ProcedureInstance?.ReferenceNumber,
                    Modalidad = latest.ProcedureInstance?.ModalidadEntrada,
                    PartyRole = latest.PartyRole,
                    Email = latest.Email,
                    Provider = latest.Provider,
                    Score = latest.Score,
                    CaptureUrl = latest.CaptureUrl,
                    ValidationCount = g.Count(),
                    Attempts = latest.Attempts,
                    MaxAttempts = latest.MaxAttempts,
                };
            })
            .ToList();

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                // Estado EFECTIVO, igual que el CASE de la consulta Npgsql.
                var st = filter.Status.Trim();
                groups = groups
                    .Where(g => string.Equals(EstadoEfectivo(g.Status, g.ExpiresAt, now), st, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (filter.CreatedFrom is { } from)
                groups = groups.Where(g => g.CreatedAt >= from).ToList();
            if (filter.CreatedTo is { } to)
                groups = groups.Where(g => g.CreatedAt <= to).ToList();

            if (!string.IsNullOrWhiteSpace(filter.VigenciaEstado))
            {
                var corteVigente = now.AddDays(-BiometricRules.VigenciaDias);
                var cortePorVencer = now.AddDays(-(BiometricRules.VigenciaDias - BiometricRules.VigenciaPorVencerDias));
                groups = filter.VigenciaEstado.Trim().ToLowerInvariant() switch
                {
                    BiometricVigenciaEstados.Vigente => groups.Where(g =>
                        g.Status == BiometricEstados.Aprobado && g.ValidatedAt is { } va && va > corteVigente).ToList(),
                    BiometricVigenciaEstados.PorVencer => groups.Where(g =>
                        g.Status == BiometricEstados.Aprobado && g.ValidatedAt is { } va
                        && va > corteVigente && va <= cortePorVencer).ToList(),
                    BiometricVigenciaEstados.Vencida => groups.Where(g =>
                        g.Status == BiometricEstados.Aprobado && g.ValidatedAt is { } va && va <= corteVigente).ToList(),
                    _ => groups,
                };
            }

            if (filter.ExpiraDesde is { } expiraDesde)
            {
                var shifted = expiraDesde.AddDays(-BiometricRules.VigenciaDias);
                groups = groups.Where(g => g.ValidatedAt is { } va && va >= shifted).ToList();
            }

            if (filter.ExpiraHasta is { } expiraHasta)
            {
                var shifted = expiraHasta.AddDays(-BiometricRules.VigenciaDias);
                groups = groups.Where(g => g.ValidatedAt is { } va && va <= shifted).ToList();
            }

            if (filter.VenceEnDias is { } venceEnDias)
            {
                var corteVigente = now.AddDays(-BiometricRules.VigenciaDias);
                var corteVenceEn = now.AddDays(venceEnDias - BiometricRules.VigenciaDias);
                groups = groups.Where(g =>
                    g.Status == BiometricEstados.Aprobado && g.ValidatedAt is { } va
                    && va > corteVigente && va <= corteVenceEn).ToList();
            }
        }

        var total = groups.Count;
        var page = groups
            .OrderByDescending(g => g.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToList();
        return (page, total);
    }

    /// <summary>Fila intermedia de SqlQueryRaw para el conteo de PERSONAS por estado (KPIs).</summary>
    private sealed class BiometricPersonStatusCountSqlRow
    {
        public string Status { get; init; } = string.Empty;

        public int PersonCount { get; init; }
    }

    /// <summary>Fila intermedia de SqlQueryRaw para el DISTINCT ON agrupado (HU #11270).</summary>
    private sealed class BiometricPersonGroupSqlRow
    {
        public Guid LatestValidationId { get; init; }
        public string DocumentType { get; init; } = string.Empty;
        public string DocumentNumber { get; init; } = string.Empty;
        public string DocumentTypeNorm { get; init; } = string.Empty;
        public string DocumentNumberNorm { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ValidatedAt { get; init; }
        public DateTimeOffset? ValidUntil { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public Guid? ProcedureInstanceId { get; init; }
        public string? ReferenceNumber { get; init; }
        public string? Modalidad { get; init; }
        public string? PartyRole { get; init; }
        public string Email { get; init; } = string.Empty;
        public string Provider { get; init; } = string.Empty;
        public int? Score { get; init; }
        public string? CaptureUrl { get; init; }
        public int Attempts { get; init; }
        public int MaxAttempts { get; init; }
        public int ValidationCount { get; init; }
        public int TotalPersons { get; init; }

        public BiometricPersonGroupProjection ToProjection() => new()
        {
            LatestValidationId = LatestValidationId,
            DocumentType = DocumentType,
            DocumentNumber = DocumentNumber,
            DocumentTypeNorm = DocumentTypeNorm,
            DocumentNumberNorm = DocumentNumberNorm,
            Name = Name,
            Status = Status,
            CreatedAt = CreatedAt,
            ValidatedAt = ValidatedAt,
            ValidUntil = ValidUntil,
            ExpiresAt = ExpiresAt,
            ProcedureInstanceId = ProcedureInstanceId,
            ReferenceNumber = ReferenceNumber,
            Modalidad = Modalidad,
            PartyRole = PartyRole,
            Email = Email,
            Provider = Provider,
            Score = Score,
            CaptureUrl = CaptureUrl,
            ValidationCount = ValidationCount,
            Attempts = Attempts,
            MaxAttempts = MaxAttempts,
        };
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
            .Include(x => x.ProcedureType)
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

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsBiometricInFlightUniqueViolation(ex))
        {
            // HU #11266 — índice uq_biometric_validations_inflight_doc_norm: a lo sumo una validación
            // en vuelo por (tenant, documento Trim+Upper). Se traduce el 23505 a excepción de dominio
            // (checklist §B12); el handler responde 409 informativo.
            throw new IdentityInFlightConflictException();
        }
    }

    private const string BiometricInFlightUniqueIndex = "uq_biometric_validations_inflight_doc_norm";

    private static bool IsBiometricInFlightUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(pg.ConstraintName, BiometricInFlightUniqueIndex, StringComparison.Ordinal);

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

    public async Task<IReadOnlyList<ProcedureStateChangeEmailDispatch>?> ListEmailDispatchesAsync(
        Guid instanceId, Guid tenantId, CancellationToken ct = default)
    {
        var exists = await db.ProcedureInstances.AsNoTracking()
            .AnyAsync(x => x.Id == instanceId && x.TenantId == tenantId && x.DeletedAt == null, ct);
        if (!exists)
            return null;

        return await db.ProcedureStateChangeEmailDispatches.AsNoTracking()
            .Where(d => d.ProcedureInstanceId == instanceId && d.TenantId == tenantId)
            .OrderByDescending(d => d.QueuedAt)
            .ThenBy(d => d.RecipientRole)
            .ThenBy(d => d.RecipientKind)
            .ToListAsync(ct);
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

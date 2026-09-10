using System.Text.Json;
using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtQueries;
using Flit.Admin.Domain.PlatePreassign;
using Flit.Queries.Domain;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Flit.Tramites.Domain.Documents;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Trámites de clientes OT — lectura/escritura cross-tenant vía grants (HU #10217).
/// En PostgreSQL desactiva RLS localmente solo para joins autorizados por grant;
/// en InMemory filtra explícitamente por tenant y transit_office_id.
/// </summary>
internal sealed class OtClientProcedureRepository : IOtClientProcedureRepository
{
    // HU #10805 — field_value donde el gestor guarda el dígito de preferencia de placa (0-9).
    private const string PlatePreferredLastDigitFieldKey = "plate_preferred_last_digit";

    private readonly FlitDbContext _context;
    private readonly ITramiteTransitionPublisher _transitionPublisher;
    private readonly IPlateRangeRepository? _plateRepo;

    public OtClientProcedureRepository(
        FlitDbContext context,
        ITramiteTransitionPublisher transitionPublisher,
        IPlateRangeRepository? plateRepo = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _transitionPublisher = transitionPublisher ?? throw new ArgumentNullException(nameof(transitionPublisher));
        _plateRepo = plateRepo;
    }

    public Task<PagedResult<OtClientProcedure>> ListAsync(
        Guid otTenantId,
        OtClientProcedureFilter filter,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeIdOverride,
            async transitOfficeId =>
            {
                var clientTenantIds = await ListGrantedClientTenantIdsAsync(
                    transitOfficeId,
                    cancellationToken).ConfigureAwait(false);

                if (clientTenantIds.Count == 0)
                {
                    return PagedResult<OtClientProcedure>.Empty;
                }

                return await ExecuteCrossTenantReadAsync(
                    async () =>
                    {
                        var query = BuildAccessibleQuery(transitOfficeId, clientTenantIds);
                        query = ApplyListFilters(query, filter);

                        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
                        if (totalCount == 0)
                        {
                            return PagedResult<OtClientProcedure>.Empty;
                        }

                        var ordered = ApplyListSort(query, filter);
                        var items = await ordered
                            .Skip((filter.Page - 1) * filter.PageSize)
                            .Take(filter.PageSize)
                            .Select(p => new OtClientProcedure
                            {
                                Id = p.Id,
                                ClientTenantId = p.TenantId,
                                ProcedureTypeId = p.ProcedureTypeId,
                                ReferenceNumber = p.ReferenceNumber,
                                Status = p.Status,
                                Familia = (p.ProcedureType != null ? p.ProcedureType.Family : ""),
                                PlateFlowStatus = p.PlateFlowStatus,
                                PlateAssignedAt = p.PlateAssignedAt,
                                PlateUpdatedAt = p.PlateUpdatedAt,
                                // HU #10804 — soat_estado por fila (para ocultar Aprobar/Rechazar en el frontend
                                // hasta que la placa esté 'asignado' con SOAT 'vigente'). Lectura cross-tenant
                                // permitida bajo el 'SET LOCAL row_security = off' de ExecuteCrossTenantReadAsync.
                                SoatEstado = _context.ProcedureInstanceFieldValues
                                    .Where(f => f.ProcedureInstanceId == p.Id
                                        && f.FieldKey == Flit.Tramites.Domain.Tramites.Services.SoatGate.FieldKey)
                                    .Select(f => f.ValueText)
                                    .FirstOrDefault(),
                                // HU #10805 — dígito de preferencia (guía para el OT al asignar placa).
                                PlatePreferredLastDigit = _context.ProcedureInstanceFieldValues
                                    .Where(f => f.ProcedureInstanceId == p.Id
                                        && f.FieldKey == PlatePreferredLastDigitFieldKey)
                                    .Select(f => f.ValueText)
                                    .FirstOrDefault(),
                                SoatPagado = _context.ProcedureInstanceFieldValues
                                    .Any(f => f.ProcedureInstanceId == p.Id
                                        && f.FieldKey == Flit.Tramites.Domain.Tramites.Estados.PlateFlowCheckFields.SoatPagado
                                        && f.ValueText == "true"),
                                ImpuestoDepartamentalPagado = _context.ProcedureInstanceFieldValues
                                    .Any(f => f.ProcedureInstanceId == p.Id
                                        && f.FieldKey == Flit.Tramites.Domain.Tramites.Estados.PlateFlowCheckFields.ImpuestoDepartamentalPagado
                                        && f.ValueText == "true"),
                                TransitOfficeId = p.TransitOfficeId,
                                CreatedAt = p.CreatedAt,
                                SubmittedAt = p.SubmittedAt,
                                Prioritario = p.Prioritario,
                                // Columnas denormalizadas para la grilla OT (VIN/placa/actores/gestor).
                                Placa = p.Plate,
                                Vin = p.Vin,
                                VendedorNombre = p.VendedorNombre,
                                CompradorNombre = p.CompradorNombre,
                                GestorNombre = _context.Users
                                    .Where(u => u.Id == p.CreatedByUserId)
                                    .Select(u => u.DisplayName)
                                    .FirstOrDefault(),
                            })
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        var enriched = await EnrichDisplayNamesAsync(items, cancellationToken)
                            .ConfigureAwait(false);

                        return new PagedResult<OtClientProcedure>(enriched, totalCount);
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OtClientProcedure?> GetByIdAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default) =>
        GetByIdAsync(otTenantId, procedureInstanceId, transitOfficeIdOverride: null, cancellationToken);

    public Task<OtClientProcedure?> GetByIdAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeIdOverride,
            transitOfficeId => FindAccessibleProcedureAsync(
                transitOfficeId,
                procedureInstanceId,
                cancellationToken),
            cancellationToken);

    public Task<OtBandejaHealth?> GetDeliveryHealthAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeIdOverride,
            async transitOfficeId =>
            {
                var grantedClientTenantIds = await ListGrantedClientTenantIdsAsync(
                    transitOfficeId,
                    cancellationToken).ConfigureAwait(false);

                return await ExecuteCrossTenantReadAsync(
                    async () =>
                    {
                        // Todos los 'entregado' dirigidos a este organismo, con o sin grant vigente:
                        // los "sin grant" son precisamente los que la bandeja no muestra (R09).
                        var delivered = _context.ProcedureInstances
                            .Include(x => x.ProcedureType)
                            .AsNoTracking()
                            .Where(p => p.DeletedAt == null
                                && p.Status == TramiteEstado.Entregado
                                && p.TransitOfficeId == transitOfficeId);

                        var deliveredTotal = await delivered
                            .CountAsync(cancellationToken).ConfigureAwait(false);

                        var deliveredWithGrant = grantedClientTenantIds.Count == 0
                            ? 0
                            : await delivered
                                .Where(p => grantedClientTenantIds.Contains(p.TenantId))
                                .CountAsync(cancellationToken).ConfigureAwait(false);

                        return (OtBandejaHealth?)new OtBandejaHealth(
                            transitOfficeId,
                            deliveredTotal,
                            deliveredWithGrant,
                            deliveredTotal - deliveredWithGrant);
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>Valor público para pedir los trámites que NO están en ruta de placa.</summary>
    public const string PlateFlowSinRuta = "sin_ruta";

    /// <inheritdoc />
    public Task<IReadOnlyList<QueryFieldDto>?> GetBandejaFilterFieldsAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeIdOverride,
            async transitOfficeId =>
            {
                var clientTenantIds = await ListGrantedClientTenantIdsAsync(
                    transitOfficeId,
                    cancellationToken).ConfigureAwait(false);

                return await ExecuteCrossTenantReadAsync(
                    async () =>
                    {
                        // Las empresas con grant vigente, TODAS y no solo las que tienen trámites
                        // ahora mismo: el contenido del filtro no debe cambiar según lo que haya en
                        // la bandeja hoy, o el mismo desplegable ofrecería cosas distintas cada día.
                        var empresas = await _context.Tenants
                            .AsNoTracking()
                            .Where(t => clientTenantIds.Contains(t.Id))
                            .Select(t => new { t.Id, t.LegalName })
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        var empresaOptions = empresas
                            .OrderBy(t => t.LegalName, StringComparer.OrdinalIgnoreCase)
                            .Select(t => new QueryFieldOptionDto(t.Id.ToString(), t.LegalName))
                            .ToList();

                        // Los tipos que este organismo ha recibido de verdad. Aquí sí se mira lo
                        // recibido y no el catálogo entero: los 21 tipos de ADR-0050 en una lista
                        // plana obligarían a buscar entre tipos que este organismo nunca tramita.
                        var tipoIds = clientTenantIds.Count == 0
                            ? []
                            : await _context.ProcedureInstances
                                .AsNoTracking()
                                .Where(p => p.DeletedAt == null
                                    && p.TransitOfficeId == transitOfficeId
                                    && clientTenantIds.Contains(p.TenantId))
                                .Select(p => p.ProcedureTypeId)
                                .Distinct()
                                .ToListAsync(cancellationToken)
                                .ConfigureAwait(false);

                        var tipos = await _context.ProcedureTypes
                            .AsNoTracking()
                            .Where(t => tipoIds.Contains(t.Id))
                            .Select(t => new { t.Id, t.Name, t.Family })
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        // Por Id y no por código, igual que el catálogo de Consultas del organismo:
                        // dentro de la superficie OT un tipo de trámite se nombra siempre por su Id.
                        var tipoOptions = TipoTramiteOptionCatalog.Build(
                            tipos.Select(t => (t.Id.ToString(), t.Name, (string?)t.Family)));

                        return (IReadOnlyList<QueryFieldDto>?)OtBandejaQueryFieldCatalog.Fields
                            // Un campo de opciones cuyo catálogo salió vacío NO se ofrece: un filtro
                            // que solo puede devolver cero se lee como que el dato no existe.
                            .Where(f => f.Id != OtBandejaQueryFieldCatalog.Empresa
                                    || empresaOptions.Count > 0)
                            .Where(f => f.Id != OtBandejaQueryFieldCatalog.TipoTramite
                                    || tipoOptions.Count > 0)
                            .Select(f => f.Id switch
                            {
                                OtBandejaQueryFieldCatalog.Empresa => f with { Options = empresaOptions },
                                OtBandejaQueryFieldCatalog.TipoTramite => f with { Options = tipoOptions },
                                _ => f,
                            })
                            .ToList();
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OtBandejaCounters?> GetBandejaCountersAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeIdOverride,
            async transitOfficeId =>
            {
                var grantedClientTenantIds = await ListGrantedClientTenantIdsAsync(
                    transitOfficeId,
                    cancellationToken).ConfigureAwait(false);

                // Sin grants no hay bandeja que contar: todo cero, y sin pegarle a la base.
                if (grantedClientTenantIds.Count == 0)
                {
                    return (OtBandejaCounters?)new OtBandejaCounters(0, 0, 0, 0, 0, 0);
                }

                return await ExecuteCrossTenantReadAsync(
                    async () =>
                    {
                        // Mismo universo que la bandeja: dirigidos a este organismo y con grant
                        // vigente. Si el conteo mirara más ancho que el listado, las tarjetas
                        // prometerían filas que al pulsar no aparecerían.
                        var accesibles = _context.ProcedureInstances
                            .AsNoTracking()
                            .Where(p => p.DeletedAt == null
                                && p.TransitOfficeId == transitOfficeId
                                && grantedClientTenantIds.Contains(p.TenantId));

                        // UNA consulta agrupada en vez de cinco COUNT: la bandeja los pide juntos y
                        // cinco viajes a la base para pintar una tira de cabecera no se justifican.
                        var porClase = await accesibles
                            .GroupBy(p => new { p.Status, p.PlateFlowStatus })
                            .Select(g => new
                            {
                                g.Key.Status,
                                g.Key.PlateFlowStatus,
                                Total = g.Count(),
                            })
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        var sinAsignarPlaca = 0;
                        var conPlacaAsignada = 0;
                        var aprobados = 0;
                        var rechazados = 0;
                        var sinGestion = 0;
                        var revocados = 0;

                        foreach (var fila in porClase)
                        {
                            var entregado = string.Equals(
                                fila.Status, TramiteEstado.Entregado, StringComparison.Ordinal);

                            if (entregado && fila.PlateFlowStatus == PlateFlowStatus.Preasignado)
                            {
                                sinAsignarPlaca += fila.Total;
                            }

                            if (entregado
                                && (fila.PlateFlowStatus == PlateFlowStatus.Asignado
                                    || fila.PlateFlowStatus == PlateFlowStatus.Terminado))
                            {
                                conPlacaAsignada += fila.Total;
                            }

                            // Sin gestión: entregado y fuera de la ruta de placa. Un trámite ya
                            // preasignado SÍ se está gestionando —el organismo tiene que ponerle
                            // placa—, así que contarlo aquí inflaría la tarjeta que dice "nadie ha
                            // empezado esto".
                            if (entregado && fila.PlateFlowStatus is null)
                            {
                                sinGestion += fila.Total;
                            }

                            if (string.Equals(fila.Status, TramiteEstado.Aprobado, StringComparison.Ordinal))
                            {
                                aprobados += fila.Total;
                            }

                            if (string.Equals(fila.Status, TramiteEstado.Rechazado, StringComparison.Ordinal))
                            {
                                rechazados += fila.Total;
                            }

                            // HU #12166/#12168 (Feature #12156) — Aprobados que el OT revocó.
                            if (string.Equals(fila.Status, TramiteEstado.Revocado, StringComparison.Ordinal))
                            {
                                revocados += fila.Total;
                            }
                        }

                        return (OtBandejaCounters?)new OtBandejaCounters(
                            sinAsignarPlaca,
                            conPlacaAsignada,
                            aprobados,
                            rechazados,
                            sinGestion,
                            revocados);
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OtClientProcedure?> ApproveAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        Guid? approvedBy,
        string source,
        Guid? mandateSignerId = null,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            otTenantId,
            procedureInstanceId,
            TramiteEstado.Aprobado,
            approvedBy,
            reason: null,
            source,
            cancellationToken,
            mandateSignerId,
            transitOfficeIdOverride: transitOfficeIdOverride);

    public Task<OtClientProcedure?> RejectAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        Guid? rejectedBy,
        string source,
        Guid? transitOfficeIdOverride = null,
        IReadOnlyList<Guid>? rejectionReasonIds = null,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            otTenantId,
            procedureInstanceId,
            TramiteEstado.Rechazado,
            rejectedBy,
            reason,
            source,
            cancellationToken,
            transitOfficeIdOverride: transitOfficeIdOverride,
            rejectionReasonIds: rejectionReasonIds);

    // Observación subsanable: destino 'rechazado' con checklist HÍBRIDO (motivo + items).
    public Task<OtClientProcedure?> ObserveAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        IReadOnlyList<OtProcedureObservationItem> items,
        Guid? observedBy,
        string source,
        Guid? transitOfficeIdOverride = null,
        IReadOnlyList<Guid>? rejectionReasonIds = null,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            otTenantId,
            procedureInstanceId,
            TramiteEstado.Rechazado,
            observedBy,
            reason,
            source,
            cancellationToken,
            items: items,
            transitOfficeIdOverride: transitOfficeIdOverride,
            rejectionReasonIds: rejectionReasonIds);

    // La decisión del OT (aprobar/rechazar/observar) aplica SIEMPRE desde 'entregado' (máquina == develop).
    // La ruta de placa no cambia el status: su progreso vive en plate_flow_status (sub-estado interno,
    // HU #10785).
    private async Task<OtClientProcedure?> TransitionAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string targetStatus,
        Guid? changedBy,
        string? reason,
        string source,
        CancellationToken cancellationToken,
        Guid? mandateSignerId = null,
        IReadOnlyList<OtProcedureObservationItem>? items = null,
        Guid? transitOfficeIdOverride = null,
        IReadOnlyList<Guid>? rejectionReasonIds = null)
    {
        var accessible = await ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeIdOverride,
            transitOfficeId => FindAccessibleProcedureAsync(
                transitOfficeId,
                procedureInstanceId,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (accessible is null)
        {
            return null;
        }

        return await ExecuteInClientTenantScopeAsync(
            accessible.ClientTenantId,
            async () =>
            {
                var entity = await _context.ProcedureInstances
                    .FirstOrDefaultAsync(
                        p => p.Id == procedureInstanceId
                            && p.TenantId == accessible.ClientTenantId
                            && p.DeletedAt == null,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return null;
                }

                var fromStatus = entity.Status;

                // N 03 (ADR-0022): la decisión OT obedece la máquina de estados única sobre el estado
                // ACTUAL (entregado→aprobado|rechazado); si no es válida, no transiciona.
                if (!TramiteStateMachine.IsValidTransition(fromStatus, targetStatus))
                {
                    return null;
                }

                // Sub-flujo de placa: el OT solo aprueba/rechaza en ruta estándar (null) o Terminado.
                // Sin asignar (preasignado) y Asignado bloquean la decisión OT.
                if ((targetStatus == TramiteEstado.Aprobado || targetStatus == TramiteEstado.Rechazado)
                    && !PlateFlowStatus.PermiteDecisionOt(entity.PlateFlowStatus))
                {
                    return null;
                }

                var resolvedChangedBy = await ResolveChangedByAsync(changedBy, cancellationToken)
                    .ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                entity.Status = targetStatus;
                entity.UpdatedAt = now;
                entity.UpdatedBy = resolvedChangedBy;

                // ADR-0036 §D9 (HU #10916) — al aprobar, persistir el mandatario resuelto en el MISMO save
                // que el status (el firmante ya se resolvió/eligió en el endpoint). Solo en aprobación.
                if (targetStatus == TramiteEstado.Aprobado && mandateSignerId is not null)
                {
                    entity.MandateSignerId = mandateSignerId;
                }

                // Feature #10587 / HU #10785 — la decisión del OT parte SIEMPRE de 'entregado' (== develop):
                // no hay hito sintético asignado→entregado (el trámite nunca salió de 'entregado'; el
                // progreso de placa fue un sub-estado interno).
                var effectiveFrom = fromStatus;

                // Feature #10587 — al aprobar un trámite de la ruta de placa, la placa reservada pasa a
                // utilizada (terminal); al rechazar, se libera y vuelve al inventario (disponible). En ambos
                // casos el sub-flujo de placa termina: se limpia plate_flow_status.
                if (targetStatus == TramiteEstado.Aprobado || targetStatus == TramiteEstado.Rechazado)
                {
                    var plateDetail = await _context.PlateRangeDetails
                        .FirstOrDefaultAsync(
                            d => d.ProcedureInstanceId == procedureInstanceId
                                && d.State == Flit.Admin.Domain.PlatePreassign.PlateState.Preasignada,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (plateDetail is not null)
                    {
                        if (targetStatus == TramiteEstado.Aprobado)
                        {
                            plateDetail.State = Flit.Admin.Domain.PlatePreassign.PlateState.Utilizada;
                            plateDetail.UsedAt = now;
                        }
                        else
                        {
                            plateDetail.State = Flit.Admin.Domain.PlatePreassign.PlateState.Disponible;
                            plateDetail.ProcedureInstanceId = null;
                            plateDetail.ReservedAt = null;
                        }
                        plateDetail.UpdatedAt = now;
                    }

                    entity.PlateFlowStatus = null;
                }

                // Feature #10701 / HU #10860 — la decisión del OT (aprobar/rechazar) invalida los
                // consolidados persistidos (maestro y wizard): la próxima generación los regenerará.
                entity.InvalidarConsolidados();

                // RNF01 — la decisión del OT también se publica hacia webhooks en la MISMA unidad
                // de trabajo (antes este flujo no notificaba; solo el submit lo hacía).
                await _transitionPublisher.EnqueueAsync(
                    new TramiteTransitionRecord(
                        accessible.ClientTenantId,
                        entity.Id,
                        effectiveFrom,
                        targetStatus,
                        reason,
                        resolvedChangedBy,
                        now),
                    cancellationToken).ConfigureAwait(false);

                // Snapshot de field_values al observar/rechazar con checklist: baseline del diff
                // de re-radicación (también se recaptura al activar POST /subsanar).
                IReadOnlyDictionary<string, string?>? fieldSnapshot = null;
                if (items is not null)
                {
                    var fieldValues = await _context.ProcedureInstanceFieldValues
                        .AsNoTracking()
                        .Where(f => f.ProcedureInstanceId == procedureInstanceId)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    fieldSnapshot = Flit.Tramites.Domain.Tramites.Services.FieldValueSnapshot.Capture(fieldValues);
                }

                // El historial se escribe aquí (no vía ITramiteTransitionRecorder) para conservar
                // el metadata cross-tenant (ot_tenant_id/source) dentro de la transacción RLS del
                // tenant cliente; la unificación con el recorder queda para la integración N 03.
                var historyId = Guid.NewGuid();
                _context.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
                {
                    Id = historyId,
                    TenantId = accessible.ClientTenantId,
                    ProcedureInstanceId = entity.Id,
                    FromStatus = effectiveFrom,
                    ToStatus = targetStatus,
                    ChangedAt = now,
                    ChangedBy = resolvedChangedBy,
                    Reason = reason,
                    Metadata = BuildStatusHistoryMetadata(otTenantId, source, reason, items, fieldSnapshot),
                });

                // Causales del catálogo, colgando del evento de rechazo. Van en el MISMO save que la
                // transición: un rechazo cuyas causales no se guardaran dejaría el reporte de motivos
                // contando de menos sin que nadie lo note.
                if (rejectionReasonIds is { Count: > 0 } && targetStatus == TramiteEstado.Rechazado)
                {
                    foreach (var reasonId in rejectionReasonIds.Distinct())
                    {
                        _context.ProcedureInstanceRejectionReasons.Add(new ProcedureInstanceRejectionReason
                        {
                            Id = Guid.NewGuid(),
                            TenantId = accessible.ClientTenantId,
                            ProcedureInstanceId = entity.Id,
                            StatusHistoryId = historyId,
                            RejectionReasonId = reasonId,
                            CreatedAt = now,
                            CreatedBy = resolvedChangedBy,
                        });
                    }
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var mapped = Map(entity);
                var enriched = await EnrichDisplayNamesAsync([mapped], cancellationToken)
                    .ConfigureAwait(false);
                return enriched[0];
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Shape del metadata de <c>procedure_instance_status_history</c>. Con checklist/observación
    /// agrega motivo + items + fieldSnapshot; sin ello, solo auditoría cross-tenant.
    /// </summary>
    private static string BuildStatusHistoryMetadata(
        Guid otTenantId,
        string source,
        string? reason,
        IReadOnlyList<OtProcedureObservationItem>? items,
        IReadOnlyDictionary<string, string?>? fieldSnapshot = null)
    {
        if (items is null && fieldSnapshot is null)
        {
            return JsonSerializer.Serialize(new
            {
                ot_tenant_id = otTenantId,
                approver_tenant_id = otTenantId,
                source,
            });
        }

        return JsonSerializer.Serialize(new
        {
            ot_tenant_id = otTenantId,
            approver_tenant_id = otTenantId,
            source,
            motivo = reason,
            items = (items ?? []).Select(i => new { campo = i.Campo, detalle = i.Detalle }),
            fieldSnapshot,
        });
    }

    // HU #10654 (Feature #10587 / HU #10785) — el OT asigna una placa a un trámite de la ruta de placa en
    // sub-estado 'preasignado' (Flujo B): reserva la placa, la escribe en field_values (el trigger lo
    // permite con plate_flow_status='preasignado') y avanza el SUB-ESTADO preasignado→asignado. El status
    // global permanece en 'entregado' (no hay transición de la máquina de estados).
    public async Task<PlateAssignmentOutcome> AssignPlateAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string plate,
        Guid? changedBy,
        string source,
        bool outOfRange = false,
        CancellationToken cancellationToken = default)
    {
        if (_plateRepo is null || string.IsNullOrWhiteSpace(plate))
        {
            return PlateAssignmentOutcome.Fail(PlateAssignmentFailure.MissingPlate);
        }

        var accessible = await ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeId => FindAccessibleProcedureAsync(transitOfficeId, procedureInstanceId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (accessible is null || accessible.TransitOfficeId is not { } officeId)
        {
            return PlateAssignmentOutcome.Fail(PlateAssignmentFailure.ProcedureNotAccessible);
        }

        // Una placa no puede estar viva en dos trámites a la vez. La búsqueda es GLOBAL (cualquier
        // compañía u OT), por eso va fuera del scope de tenant y con lectura cross-tenant: un trámite
        // de otra compañía que ya ocupa la placa es invisible bajo RLS y el conflicto pasaría de largo.
        var enUso = await FindProcedureHoldingPlateAsync(plate, procedureInstanceId, cancellationToken)
            .ConfigureAwait(false);
        if (enUso is not null)
        {
            return PlateAssignmentOutcome.Fail(
                PlateAssignmentFailure.PlateInUseByAnotherProcedure,
                $"La placa {plate.Trim().ToUpperInvariant()} ya está registrada en el trámite {enUso.ReferenceNumber} ({enUso.Status}). No se puede asignar a otro trámite mientras ese siga abierto.");
        }

        return await ExecuteInClientTenantScopeAsync(
            accessible.ClientTenantId,
            async () =>
            {
                var entity = await _context.ProcedureInstances
                    .FirstOrDefaultAsync(
                        p => p.Id == procedureInstanceId
                            && p.TenantId == accessible.ClientTenantId
                            && p.DeletedAt == null,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return PlateAssignmentOutcome.Fail(PlateAssignmentFailure.ProcedureNotAccessible);
                }

                if (entity.Status != TramiteEstado.Entregado
                    || entity.PlateFlowStatus != PlateFlowStatus.Preasignado)
                {
                    return PlateAssignmentOutcome.Fail(PlateAssignmentFailure.NotPreassigned);
                }

                // HU #10800 — Flujo B: el OT elige una placa del rango (TryReserve, solo placas disponibles)
                // o registra una placa FUERA DE RANGO (ReserveOutOfRange, la crea como rango ad-hoc de 1 placa).
                if (outOfRange)
                {
                    var outResult = await _plateRepo
                        .ReserveOutOfRangePlateAsync(accessible.ClientTenantId, officeId, plate, procedureInstanceId, cancellationToken)
                        .ConfigureAwait(false);
                    if (!outResult.Success)
                    {
                        // Fuera de rango solo falla por formato o porque la placa ya está en el
                        // inventario del OT; el repo ya redactó la causa exacta y se propaga tal cual
                        // (no se re-consulta: la transacción puede venir abortada por el fallo).
                        return PlateAssignmentOutcome.Fail(
                            PlateAssignmentFailure.PlateAlreadyAssigned,
                            outResult.Error);
                    }
                }
                else
                {
                    var reserved = await _plateRepo
                        .TryReservePlateAsync(accessible.ClientTenantId, officeId, plate, procedureInstanceId, cancellationToken)
                        .ConfigureAwait(false);
                    if (!reserved)
                    {
                        // La reserva falla por dos motivos muy distintos y el operador necesita
                        // distinguirlos: que la placa ya esté tomada (hay que elegir otra) o que no
                        // pertenezca a ningún rango del OT (hay que registrarla fuera de rango).
                        var yaRegistrada = await _context.PlateRangeDetails
                            .AsNoTracking()
                            .AnyAsync(
                                d => d.TransitOfficeId == officeId
                                    && d.Plate == plate.Trim().ToUpperInvariant()
                                    && d.ProcedureInstanceId != null
                                    && d.ProcedureInstanceId != procedureInstanceId,
                                cancellationToken)
                            .ConfigureAwait(false);

                        return PlateAssignmentOutcome.Fail(yaRegistrada
                            ? PlateAssignmentFailure.PlateAlreadyAssigned
                            : PlateAssignmentFailure.PlateNotAvailable);
                    }
                }

                // Escribe la placa en field_values ESTANDO en preasignado (el trigger lo permite) y
                // persiste antes de cambiar el estado (evita el orden de operaciones del trigger).
                var normalizedPlate = plate.Trim().ToUpperInvariant();
                var fv = await _context.ProcedureInstanceFieldValues
                    .FirstOrDefaultAsync(
                        f => f.ProcedureInstanceId == procedureInstanceId && f.FieldKey == "plate",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (fv is null)
                {
                    _context.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
                    {
                        Id = Guid.NewGuid(),
                        ProcedureInstanceId = procedureInstanceId,
                        TenantId = accessible.ClientTenantId,
                        FieldKey = "plate",
                        ValueText = normalizedPlate,
                    });
                }
                else
                {
                    fv.ValueText = normalizedPlate;
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // tr_procedure_instance_field_values_denorm copia la placa a procedure_instances, y ese
                // UPDATE dispara tr_procedure_instances_row_version. El row_version que EF tiene cargado
                // queda obsoleto, así que el UPDATE del sub-estado afectaría 0 filas y reventaría con
                // DbUpdateConcurrencyException (500). Se recarga el token antes de tocar la instancia.
                await _context.Entry(entity).ReloadAsync(cancellationToken).ConfigureAwait(false);

                var resolvedChangedBy = await ResolveChangedByAsync(changedBy, cancellationToken).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                // Sub-estado interno: preasignado→asignado. El status global NO cambia (queda 'entregado'),
                // así que no se emite transición de la máquina de estados ni fila de historial de status
                // (evita registrar aristas que la máquina no contempla). La trazabilidad de la placa queda
                // en plate_range_details (reserva) y en el field_value 'plate'.
                entity.PlateFlowStatus = PlateFlowStatus.Asignado;
                // HU #12165/#12167 (Feature #12156) — momento de la asignación ORIGINAL: base de la
                // ventana de 1 hora de UpdatePlateAsync. UpdatedAt no sirve (lo pisa cualquier otra
                // escritura); esta columna solo la toca este método.
                entity.PlateAssignedAt = now;
                entity.UpdatedAt = now;
                entity.UpdatedBy = resolvedChangedBy;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var mapped = Map(entity);
                var enriched = await EnrichDisplayNamesAsync([mapped], cancellationToken).ConfigureAwait(false);
                return PlateAssignmentOutcome.Ok(enriched[0]);
            },
            cancellationToken).ConfigureAwait(false);
    }

    // HU #12167 (Feature #12156) — el OT corrige la placa dentro de la ventana de 1 hora desde
    // plate_assigned_at, una única vez (plate_updated_at nulo). NO reutiliza AssignPlateAsync: ese
    // método exige PlateFlowStatus.Preasignado (la placa aún no existe); aquí la placa YA está asignada
    // (Asignado o incluso ya entregado/aprobado — la HU no acota el sub-estado, solo la ventana de
    // tiempo) y solo se corrige el valor.
    public async Task<PlateAssignmentOutcome> UpdatePlateAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string plate,
        Guid? changedBy,
        string source,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            return PlateAssignmentOutcome.Fail(PlateAssignmentFailure.MissingPlate);
        }

        var accessible = await ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeId => FindAccessibleProcedureAsync(transitOfficeId, procedureInstanceId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (accessible is null)
        {
            return PlateAssignmentOutcome.Fail(PlateAssignmentFailure.ProcedureNotAccessible);
        }

        // Misma regla global de AssignPlateAsync: una placa no puede estar viva en dos trámites a la vez.
        var enUso = await FindProcedureHoldingPlateAsync(plate, procedureInstanceId, cancellationToken)
            .ConfigureAwait(false);
        if (enUso is not null)
        {
            return PlateAssignmentOutcome.Fail(
                PlateAssignmentFailure.PlateInUseByAnotherProcedure,
                $"La placa {plate.Trim().ToUpperInvariant()} ya está registrada en el trámite {enUso.ReferenceNumber} ({enUso.Status}). No se puede asignar a otro trámite mientras ese siga abierto.");
        }

        return await ExecuteInClientTenantScopeAsync(
            accessible.ClientTenantId,
            async () =>
            {
                var entity = await _context.ProcedureInstances
                    .FirstOrDefaultAsync(
                        p => p.Id == procedureInstanceId
                            && p.TenantId == accessible.ClientTenantId
                            && p.DeletedAt == null,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return PlateAssignmentOutcome.Fail(PlateAssignmentFailure.ProcedureNotAccessible);
                }

                var now = DateTimeOffset.UtcNow;

                // AC2 — sin asignación previa registrada, o pasada la hora: rechaza.
                if (entity.PlateAssignedAt is not { } assignedAt || now - assignedAt > TimeSpan.FromHours(1))
                {
                    return PlateAssignmentOutcome.Fail(PlateAssignmentFailure.PlateUpdateWindowExpired);
                }

                // AC3 — una única oportunidad, aunque siga dentro de la hora.
                if (entity.PlateUpdatedAt is not null)
                {
                    return PlateAssignmentOutcome.Fail(PlateAssignmentFailure.PlateUpdateAlreadyUsed);
                }

                var previousPlate = entity.Plate;
                var normalizedPlate = plate.Trim().ToUpperInvariant();

                // Mismo orden que AssignPlateAsync: escribe field_values y persiste ANTES de tocar la
                // instancia (el trigger de denormalización dispara tr_procedure_instances_row_version;
                // recargar después evita el DbUpdateConcurrencyException del token ya obsoleto).
                var fv = await _context.ProcedureInstanceFieldValues
                    .FirstOrDefaultAsync(
                        f => f.ProcedureInstanceId == procedureInstanceId && f.FieldKey == "plate",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (fv is null)
                {
                    _context.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
                    {
                        Id = Guid.NewGuid(),
                        ProcedureInstanceId = procedureInstanceId,
                        TenantId = accessible.ClientTenantId,
                        FieldKey = "plate",
                        ValueText = normalizedPlate,
                    });
                }
                else
                {
                    fv.ValueText = normalizedPlate;
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await _context.Entry(entity).ReloadAsync(cancellationToken).ConfigureAwait(false);

                var resolvedChangedBy = await ResolveChangedByAsync(changedBy, cancellationToken).ConfigureAwait(false);
                entity.PlateUpdatedAt = now;
                entity.UpdatedAt = now;
                entity.UpdatedBy = resolvedChangedBy;

                // AC1 — historial de la corrección (placa anterior, nueva, usuario, fecha, hora). No hay
                // transición de status: se registra como evento de bitácora (mismo mecanismo del resto
                // del feature), no como fila de procedure_instance_status_history.
                _context.ProcedureInstanceEvents.Add(new ProcedureInstanceEvent
                {
                    Id = Guid.NewGuid(),
                    TenantId = accessible.ClientTenantId,
                    ProcedureInstanceId = procedureInstanceId,
                    Tipo = "placa_corregida_ot",
                    CreatedAt = now,
                    CreatedBy = resolvedChangedBy,
                    Payload = JsonSerializer.Serialize(new
                    {
                        placa_anterior = previousPlate,
                        placa_nueva = normalizedPlate,
                        ot_tenant_id = otTenantId,
                        source,
                    }),
                });

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var mapped = Map(entity);
                var enriched = await EnrichDisplayNamesAsync([mapped], cancellationToken).ConfigureAwait(false);
                return PlateAssignmentOutcome.Ok(enriched[0]);
            },
            cancellationToken).ConfigureAwait(false);
    }

    // HU #10655 (Feature #10587 / HU #10785) — el OT revoca la preasignación: libera la placa
    // (preasignada→revocada) y, si el sub-estado era 'asignado', lo devuelve a 'preasignado' para
    // reasignar. El status global permanece 'entregado' (no hay transición de la máquina de estados).
    public async Task<OtClientProcedure?> RevokePlateAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        Guid? changedBy,
        string source,
        CancellationToken cancellationToken = default)
    {
        var accessible = await ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeId => FindAccessibleProcedureAsync(transitOfficeId, procedureInstanceId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (accessible is null)
        {
            return null;
        }

        return await ExecuteInClientTenantScopeAsync(
            accessible.ClientTenantId,
            async () =>
            {
                var entity = await _context.ProcedureInstances
                    .FirstOrDefaultAsync(
                        p => p.Id == procedureInstanceId
                            && p.TenantId == accessible.ClientTenantId
                            && p.DeletedAt == null,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null
                    || entity.Status != TramiteEstado.Entregado
                    || entity.PlateFlowStatus is not (PlateFlowStatus.Preasignado or PlateFlowStatus.Asignado))
                {
                    return null;
                }

                var now = DateTimeOffset.UtcNow;
                var resolvedChangedBy = await ResolveChangedByAsync(changedBy, cancellationToken).ConfigureAwait(false);

                var plateDetail = await _context.PlateRangeDetails
                    .FirstOrDefaultAsync(
                        d => d.ProcedureInstanceId == procedureInstanceId
                            && d.State == Flit.Admin.Domain.PlatePreassign.PlateState.Preasignada,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (plateDetail is not null)
                {
                    plateDetail.State = Flit.Admin.Domain.PlatePreassign.PlateState.Revocada;
                    plateDetail.ProcedureInstanceId = null;
                    plateDetail.ReservedAt = null;
                    plateDetail.UpdatedAt = now;
                }

                // Sub-estado interno: si estaba 'asignado', revocar lo devuelve a 'preasignado' para
                // reasignar placa. El status global permanece 'entregado' (sin transición de la máquina).
                if (entity.PlateFlowStatus == PlateFlowStatus.Asignado)
                {
                    entity.PlateFlowStatus = PlateFlowStatus.Preasignado;
                    entity.UpdatedAt = now;
                    entity.UpdatedBy = resolvedChangedBy;
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var mapped = Map(entity);
                var enriched = await EnrichDisplayNamesAsync([mapped], cancellationToken).ConfigureAwait(false);
                return enriched[0];
            },
            cancellationToken).ConfigureAwait(false);
    }

    // HU #12166 (Feature #12156) — el OT deshace su propia aprobación. NO reutiliza TransitionAsync:
    // ese método asume la decisión 'entregado→aprobado|rechazado' (mira PlateRangeDetails en estado
    // Preasignada, que ya no existe para un trámite Aprobado — la placa quedó Utilizada al aprobar) y
    // exige PlateFlowStatus.PermiteDecisionOt, que no aplica aquí. 'aprobado→revocado' es una
    // transición propia con su propio esqueleto, espejo estructural de TransitionAsync/RevokePlateAsync.
    public async Task<OtClientProcedure?> RevokeAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string? reason,
        Guid? changedBy,
        string source,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var accessible = await ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeIdOverride,
            transitOfficeId => FindAccessibleProcedureAsync(transitOfficeId, procedureInstanceId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (accessible is null)
        {
            return null;
        }

        return await ExecuteInClientTenantScopeAsync(
            accessible.ClientTenantId,
            async () =>
            {
                var entity = await _context.ProcedureInstances
                    .FirstOrDefaultAsync(
                        p => p.Id == procedureInstanceId
                            && p.TenantId == accessible.ClientTenantId
                            && p.DeletedAt == null,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return null;
                }

                var fromStatus = entity.Status;

                // AC2 (HU #12166) — única transición de la máquina desde 'aprobado' es a 'revocado'; si
                // el trámite ya no está en 'aprobado' (o cualquier otro estado), IsValidTransition
                // rechaza sin necesidad de comparar contra la constante directamente.
                if (!TramiteStateMachine.IsValidTransition(fromStatus, TramiteEstado.Revocado))
                {
                    return null;
                }

                var resolvedChangedBy = await ResolveChangedByAsync(changedBy, cancellationToken)
                    .ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                entity.Status = TramiteEstado.Revocado;
                entity.UpdatedAt = now;
                entity.UpdatedBy = resolvedChangedBy;

                // AC1 — el FUR/certificados vigentes quedan como históricos: visibles, no borrados. La
                // placa se libera SIN tocar plate_range_details: 'revocado' ya está en
                // EstadosQueLiberanPlaca, y FindProcedureHoldingPlateAsync (el chequeo real de "una
                // placa no puede estar viva en dos trámites") filtra por ese conjunto.
                var attachments = await _context.ProcedureInstanceAttachments
                    .Where(a => a.ProcedureInstanceId == procedureInstanceId && !a.IsHistorico)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var attachment in attachments)
                {
                    attachment.IsHistorico = true;
                }

                _context.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    TenantId = accessible.ClientTenantId,
                    ProcedureInstanceId = entity.Id,
                    FromStatus = fromStatus,
                    ToStatus = TramiteEstado.Revocado,
                    ChangedAt = now,
                    ChangedBy = resolvedChangedBy,
                    Reason = reason,
                    Metadata = BuildStatusHistoryMetadata(otTenantId, source, reason, items: null),
                });

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var mapped = Map(entity);
                var enriched = await EnrichDisplayNamesAsync([mapped], cancellationToken).ConfigureAwait(false);
                return enriched[0];
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OtClientProcedure?> FindAccessibleProcedureAsync(
        Guid transitOfficeId,
        Guid procedureInstanceId,
        CancellationToken cancellationToken)
    {
        var clientTenantIds = await ListGrantedClientTenantIdsAsync(
            transitOfficeId,
            cancellationToken).ConfigureAwait(false);

        if (clientTenantIds.Count == 0)
        {
            return null;
        }

        return await ExecuteCrossTenantReadAsync(
            async () =>
            {
                // Solo las columnas que viven en la propia instancia. Los datos que están en
                // field_values se resuelven después con UNA lectura de la tabla: una subconsulta
                // correlacionada por atributo escalaba a más de veinte para el detalle completo.
                var mapped = await BuildAccessibleQuery(transitOfficeId, clientTenantIds)
                    .Where(p => p.Id == procedureInstanceId)
                    .Select(p => new ProcedureInstanceRow(
                        p.Id,
                        p.TenantId,
                        p.ProcedureTypeId,
                        p.ReferenceNumber,
                        p.Status,
                        // La modalidad gobierna qué causales de rechazo aplican: sin ella, el guard
                        // del rechazo descartaría causales válidas por creerlas de otro proceso.
                        p.ProcedureType != null ? p.ProcedureType.Family : "",
                        p.PlateFlowStatus,
                        p.PlateAssignedAt,
                        p.PlateUpdatedAt,
                        p.TransitOfficeId,
                        p.CreatedAt,
                        p.SubmittedAt,
                        p.Prioritario,
                        p.Plate,
                        p.Vin,
                        p.VendedorNombre,
                        p.CompradorNombre,
                        _context.Users
                            .Where(u => u.Id == p.CreatedByUserId)
                            .Select(u => u.DisplayName)
                            .FirstOrDefault()))
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (mapped is null)
                {
                    return null;
                }

                var fields = await LoadFieldValuesAsync(mapped.Id, cancellationToken)
                    .ConfigureAwait(false);

                var actors = await _context.ProcedureInstanceActors
                    .AsNoTracking()
                    .Where(a => a.ProcedureInstanceId == mapped.Id)
                    .OrderBy(a => a.ActorType)
                    .ThenBy(a => a.FullName)
                    .Select(a => new OtClientProcedureActor
                    {
                        ActorType = a.ActorType,
                        DocumentType = a.DocumentType,
                        DocumentNumber = a.DocumentNumber,
                        FullName = a.FullName,
                        Email = a.Email,
                        Phone = a.Phone,
                        PersonType = a.PersonType,
                    })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var comercial = await _context.ProcedureInstanceCommercials
                    .AsNoTracking()
                    .Where(c => c.ProcedureInstanceId == mapped.Id)
                    .Select(c => new OtClientProcedureCommercial
                    {
                        ValorVenta = c.ValorVenta,
                        Causal = c.Causal,
                        TasaImpuesto = c.TasaImpuesto,
                        Derechos = c.Derechos,
                        MetodoPago = c.MetodoPago,
                    })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                var prenda = await _context.ProcedureInstancePrendas
                    .AsNoTracking()
                    .Where(x => x.ProcedureInstanceId == mapped.Id)
                    .Select(x => new OtClientProcedurePrenda
                    {
                        Decision = x.Decision,
                        Estado = x.Estado,
                        AcreedorNombre = x.AcreedorNombre,
                        AcreedorDocumento = x.AcreedorDocumento,
                        LevantamientoEntidad = x.LevantamientoEntidad,
                    })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                var procedure = new OtClientProcedure
                {
                    Id = mapped.Id,
                    ClientTenantId = mapped.ClientTenantId,
                    ProcedureTypeId = mapped.ProcedureTypeId,
                    ReferenceNumber = mapped.ReferenceNumber,
                    Status = mapped.Status,
                    Familia = mapped.Familia,
                    PlateFlowStatus = mapped.PlateFlowStatus,
                    PlateAssignedAt = mapped.PlateAssignedAt,
                    PlateUpdatedAt = mapped.PlateUpdatedAt,
                    // HU #10804 — soat_estado también en el detalle (mismo criterio de visibilidad).
                    SoatEstado = Field(fields, Flit.Tramites.Domain.Tramites.Services.SoatGate.FieldKey),
                    // HU #10805 — dígito de preferencia también en el detalle.
                    PlatePreferredLastDigit = Field(fields, PlatePreferredLastDigitFieldKey),
                    SoatPagado = IsTrue(fields, Flit.Tramites.Domain.Tramites.Estados.PlateFlowCheckFields.SoatPagado),
                    ImpuestoDepartamentalPagado = IsTrue(
                        fields,
                        Flit.Tramites.Domain.Tramites.Estados.PlateFlowCheckFields.ImpuestoDepartamentalPagado),
                    TransitOfficeId = mapped.TransitOfficeId,
                    CreatedAt = mapped.CreatedAt,
                    SubmittedAt = mapped.SubmittedAt,
                    Prioritario = mapped.Prioritario,
                    Actors = actors,
                    Placa = mapped.Placa,
                    Vin = mapped.Vin,
                    VendedorNombre = mapped.VendedorNombre,
                    CompradorNombre = mapped.CompradorNombre,
                    GestorNombre = mapped.GestorNombre,
                    Marca = Field(fields, VehicleFieldKeys.Brand),
                    Linea = Field(fields, VehicleFieldKeys.Line),
                    // Bug #11584 — el runtime persiste el año bajo "vehicle_year"; "vehicle_model" solo
                    // aparece en datos históricos migrados, así que sirve de respaldo, no de fuente.
                    Modelo = Field(fields, VehicleFieldKeys.Year) ?? Field(fields, VehicleFieldKeys.LegacyModel),
                    Color = Field(fields, VehicleFieldKeys.Color),
                    Clase = Field(fields, VehicleFieldKeys.Class),
                    Servicio = Field(fields, VehicleFieldKeys.Service),
                    Combustible = Field(fields, VehicleFieldKeys.Fuel),
                    Carroceria = Field(fields, VehicleFieldKeys.BodyType),
                    Cilindraje = Field(fields, VehicleFieldKeys.EngineDisplacement),
                    Capacidad = Field(fields, VehicleFieldKeys.Passengers),
                    Ejes = Field(fields, VehicleFieldKeys.Axles),
                    EstadoVehiculo = Field(fields, VehicleFieldKeys.State),
                    NumeroMotor = Field(fields, VehicleFieldKeys.EngineNumber),
                    NumeroChasis = Field(fields, VehicleFieldKeys.Chassis),
                    NumeroSerie = Field(fields, VehicleFieldKeys.Series),
                    RuntSnapshot = BuildRuntSnapshot(fields),
                    TransformacionesDeclaradas = new OtClientProcedureTransformationFlags
                    {
                        Color = IsTrue(fields, MandatoObjetoComposer.CambioColor),
                        Combustible = IsTrue(fields, MandatoObjetoComposer.CambioCombustible),
                        Carroceria = IsTrue(fields, MandatoObjetoComposer.CambioCarroceria),
                    },
                    Comercial = comercial,
                    Prenda = prenda,
                };

                var enriched = await EnrichDisplayNamesAsync([procedure], cancellationToken)
                    .ConfigureAwait(false);
                return enriched[0];
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Columnas del detalle que viven en <c>procedure_instances</c> (o en un join directo).</summary>
    private sealed record ProcedureInstanceRow(
        Guid Id,
        Guid ClientTenantId,
        Guid ProcedureTypeId,
        string ReferenceNumber,
        string Status,
        string Familia,
        string? PlateFlowStatus,
        DateTimeOffset? PlateAssignedAt,
        DateTimeOffset? PlateUpdatedAt,
        Guid? TransitOfficeId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SubmittedAt,
        bool Prioritario,
        string? Placa,
        string? Vin,
        string? VendedorNombre,
        string? CompradorNombre,
        string? GestorNombre);

    /// <summary>
    /// Todos los <c>field_values</c> de la instancia en una sola lectura. La última repetición de una
    /// clave gana, que es el criterio que ya aplicaban las subconsultas con <c>FirstOrDefault</c>
    /// sobre una tabla sin orden garantizado; en la práctica la clave es única por instancia.
    /// </summary>
    private async Task<Dictionary<string, string?>> LoadFieldValuesAsync(
        Guid procedureInstanceId,
        CancellationToken cancellationToken)
    {
        var rows = await _context.ProcedureInstanceFieldValues
            .AsNoTracking()
            .Where(f => f.ProcedureInstanceId == procedureInstanceId)
            .Select(f => new { f.FieldKey, f.ValueText })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var fields = new Dictionary<string, string?>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            fields[row.FieldKey] = row.ValueText;
        }

        return fields;
    }

    /// <summary>Valor de una clave, o <c>null</c> si el trámite no la tiene. Nunca cae en otra clave.</summary>
    private static string? Field(Dictionary<string, string?> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : null;

    /// <summary>Bandera booleana persistida como texto. Mismo criterio que las consultas previas.</summary>
    private static bool IsTrue(Dictionary<string, string?> fields, string key) =>
        string.Equals(Field(fields, key), "true", StringComparison.Ordinal);

    /// <summary>
    /// Snapshot RUNT de los atributos transformables, o <c>null</c> si el trámite no capturó ninguno
    /// (borradores anteriores a la feature). Devolver un snapshot con las tres caras vacías haría
    /// creer al consumidor que el RUNT no tiene color, y no es lo mismo que no haberlo consultado.
    /// </summary>
    private static OtClientProcedureVehicleSnapshot? BuildRuntSnapshot(Dictionary<string, string?> fields)
    {
        var color = Field(fields, VehicleFieldKeys.ColorRunt);
        var combustible = Field(fields, VehicleFieldKeys.FuelRunt);
        var carroceria = Field(fields, VehicleFieldKeys.BodyTypeRunt);

        if (color is null && combustible is null && carroceria is null)
        {
            return null;
        }

        return new OtClientProcedureVehicleSnapshot
        {
            Color = color,
            Combustible = combustible,
            Carroceria = carroceria,
        };
    }
    /// <summary>
    /// Universo de trámites que este organismo puede ver. Es el cuello de botella de TODA la superficie
    /// OT: de aquí cuelgan la bandeja y <see cref="FindAccessibleProcedureAsync"/>, y de esa última el
    /// detalle por id, aprobar, rechazar, asignar placa y revocar placa.
    ///
    /// <para>El filtro de estado (HU #11945) vive aquí y no en el listado a propósito. Antes, la única
    /// defensa contra ver un borrador era que el cliente mandara un <c>status</c> en la consulta, así
    /// que bastaba con pedir la bandeja sin filtro —o pedir el detalle por id— para leer trámites que la
    /// empresa cliente todavía estaba redactando y no había enviado. Poniéndolo en la consulta base, un
    /// <c>?status=borrador</c> devuelve vacío por construcción y el detalle de un no entregado da 404,
    /// sin lógica adicional en ninguno de los consumidores.</para>
    /// </summary>
    private IQueryable<ProcedureInstance> BuildAccessibleQuery(
        Guid transitOfficeId,
        IReadOnlyList<Guid> clientTenantIds) =>
        _context.ProcedureInstances
            .AsNoTracking()
            .Where(p => p.DeletedAt == null
                && p.TransitOfficeId == transitOfficeId
                && clientTenantIds.Contains(p.TenantId)
                && TramiteEstado.RecibidosPorOrganismo.Contains(p.Status));

    private async Task<Guid?> ResolveTransitOfficeIdAsync(
        Guid otTenantId,
        CancellationToken cancellationToken)
    {
        var profile = await ExecuteInOtTenantScopeAsync(
            otTenantId,
            async () => await _context.TransitOfficeProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == otTenantId, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return profile?.TransitOfficeId;
    }

    private async Task<IReadOnlyList<Guid>> ListGrantedClientTenantIdsAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken) =>
        await ExecuteCrossTenantReadAsync(
            async () => (IReadOnlyList<Guid>)await _context.TenantTransitOfficeGrants
                .AsNoTracking()
                .Where(g => g.TransitOfficeId == transitOfficeId && g.IsEnabled)
                .Select(g => g.TenantId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    private async Task<T> ExecuteOtScopedAsync<T>(
        Guid otTenantId,
        Guid? transitOfficeIdOverride,
        Func<Guid, Task<T>> action,
        CancellationToken cancellationToken)
    {
        Guid? transitOfficeId = transitOfficeIdOverride is Guid overrideId && overrideId != Guid.Empty
            ? overrideId
            : await ResolveTransitOfficeIdAsync(otTenantId, cancellationToken).ConfigureAwait(false);

        if (transitOfficeId is null)
        {
            return typeof(T) == typeof(PagedResult<OtClientProcedure>)
                ? (T)(object)PagedResult<OtClientProcedure>.Empty
                : default!;
        }

        return await action(transitOfficeId.Value).ConfigureAwait(false);
    }

    private async Task<T> ExecuteOtScopedAsync<T>(
        Guid otTenantId,
        Func<Guid, Task<T>> action,
        CancellationToken cancellationToken) =>
        await ExecuteOtScopedAsync(otTenantId, null, action, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Primer trámite VIVO (cualquier compañía u OT) que ya tiene esta placa, o <c>null</c> si está
    /// libre. Solo rechazados y anulados liberan la placa; borradores, entregados y aprobados no.
    /// Se lee <c>procedure_instances.plate</c>, la columna denormalizada que mantienen los triggers
    /// de <c>procedure_instance_field_values</c>.
    /// </summary>
    private Task<PlateHolder?> FindProcedureHoldingPlateAsync(
        string plate,
        Guid excludedProcedureInstanceId,
        CancellationToken cancellationToken)
    {
        var normalized = plate.Trim().ToUpperInvariant();

        return ExecuteCrossTenantReadAsync(
            () => _context.ProcedureInstances
                .Include(x => x.ProcedureType)
                .AsNoTracking()
                .Where(p => p.Id != excludedProcedureInstanceId
                    && p.DeletedAt == null
                    && p.Plate == normalized
                    && !TramiteEstado.EstadosQueLiberanPlaca.Contains(p.Status))
                .OrderBy(p => p.CreatedAt)
                .Select(p => new PlateHolder(p.Id, p.ReferenceNumber, p.Status))
                .FirstOrDefaultAsync(cancellationToken),
            cancellationToken);
    }

    private sealed record PlateHolder(Guid Id, string ReferenceNumber, string Status);

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "SET LOCAL row_security = off",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }

    private async Task<T> ExecuteInOtTenantScopeAsync<T>(
        Guid otTenantId,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {otTenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }

    public async Task<T> ExecuteInClientTenantScopeAsync<T>(
        Guid clientTenantId,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {clientTenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }

    /// <summary>Evita violación FK si el JWT sub no existe en identity.users.</summary>
    private async Task<Guid?> ResolveChangedByAsync(Guid? changedBy, CancellationToken cancellationToken)
    {
        if (changedBy is null || changedBy == Guid.Empty)
        {
            return null;
        }

        var exists = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == changedBy.Value, cancellationToken)
            .ConfigureAwait(false);

        return exists ? changedBy : null;
    }

    private IQueryable<ProcedureInstance> ApplyListFilters(
        IQueryable<ProcedureInstance> query,
        OtClientProcedureFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(p => p.Status == filter.Status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.PlateFlowStatus))
        {
            var valores = filter.PlateFlowStatus
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => v.ToLowerInvariant())
                .ToList();

            // `sin_ruta` no es un valor de la columna: es la AUSENCIA de ruta de placa (null). Se
            // trata aparte para poder pedirlo junto a valores reales sin escribir dos consultas.
            var incluirSinRuta = valores.Remove(PlateFlowSinRuta);

            if (incluirSinRuta && valores.Count > 0)
            {
                query = query.Where(p =>
                    p.PlateFlowStatus == null || valores.Contains(p.PlateFlowStatus));
            }
            else if (incluirSinRuta)
            {
                query = query.Where(p => p.PlateFlowStatus == null);
            }
            else if (valores.Count > 0)
            {
                query = query.Where(p =>
                    p.PlateFlowStatus != null && valores.Contains(p.PlateFlowStatus));
            }
        }

        if (filter.ProcedureTypeId is not null)
        {
            query = query.Where(p => p.ProcedureTypeId == filter.ProcedureTypeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Vin))
        {
            var vin = filter.Vin.Trim().ToUpperInvariant();
            query = query.Where(p => p.Vin != null && p.Vin.ToUpper().Contains(vin));
        }

        if (!string.IsNullOrWhiteSpace(filter.Placa))
        {
            var placa = filter.Placa.Trim().ToUpperInvariant();
            query = query.Where(p => p.Plate != null && p.Plate.ToUpper().Contains(placa));
        }

        if (!string.IsNullOrWhiteSpace(filter.Vendedor))
        {
            var vendedor = filter.Vendedor.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.VendedorNombre != null && p.VendedorNombre.ToLower().Contains(vendedor));
        }

        if (!string.IsNullOrWhiteSpace(filter.Comprador))
        {
            var comprador = filter.Comprador.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.CompradorNombre != null && p.CompradorNombre.ToLower().Contains(comprador));
        }

        if (!string.IsNullOrWhiteSpace(filter.Gestor))
        {
            var gestor = filter.Gestor.Trim().ToLowerInvariant();
            query = query.Where(p =>
                _context.Users.Any(u =>
                    u.Id == p.CreatedByUserId
                    && u.DisplayName.ToLower().Contains(gestor)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Busqueda))
        {
            var termino = filter.Busqueda.Trim();
            var enMinusculas = termino.ToLowerInvariant();
            var enMayusculas = termino.ToUpperInvariant();
            // HU #12371 — el término se LEE como radicado (12, 0000012, FT1-0000012, ft1 12), con la
            // misma lectura que el listado del gestor: sin prefijo casa el consecutivo, con prefijo
            // el texto canónico. Si no es un radicado, ese OR se apaga.
            var (consecutivoBuscado, radicadoCanonico) = ProcedureInstanceFiltroSql.LeerBusquedaRadicado(termino);

            query = query.Where(p =>
                (consecutivoBuscado != null && p.Consecutivo == consecutivoBuscado)
                || (radicadoCanonico != null && p.ReferenceNumber.ToUpper().Replace("-", "") == radicadoCanonico)
                || (p.Plate != null && p.Plate.ToUpper().Contains(enMayusculas))
                || (p.Vin != null && p.Vin.ToUpper().Contains(enMayusculas))
                || (p.CompradorNombre != null && p.CompradorNombre.ToLower().Contains(enMinusculas))
                || (p.VendedorNombre != null && p.VendedorNombre.ToLower().Contains(enMinusculas))
                || p.Actors.Any(a => a.DocumentNumber != null
                    && a.DocumentNumber.ToLower().Contains(enMinusculas))
                || _context.Tenants.Any(t => t.Id == p.TenantId
                    && t.LegalName.ToLower().Contains(enMinusculas)));
        }

        // Rango de fechas (HU #12217). Va en SQL, no sobre la página: el total de la cabecera y el
        // recorrido del export tienen que ver el mismo universo que la tabla.
        if (filter.CreatedFrom is { } creadoDesde)
            query = query.Where(p => p.CreatedAt >= creadoDesde);

        if (filter.CreatedTo is { } creadoHasta)
            query = query.Where(p => p.CreatedAt <= creadoHasta);

        if (filter.UpdatedFrom is { } actualizadoDesde)
            query = query.Where(p => p.UpdatedAt >= actualizadoDesde);

        if (filter.UpdatedTo is { } actualizadoHasta)
            query = query.Where(p => p.UpdatedAt <= actualizadoHasta);

        if (filter.Condiciones is { Count: > 0 } condiciones)
        {
            foreach (var condicion in condiciones)
                query = ApplyCondition(query, condicion);
        }

        return query;
    }

    // ── Condiciones de la gramática de Consultas (HU #12217) ──────────────────────────────────

    /// <summary>
    /// Traduce UNA condición del catálogo de la bandeja a <c>WHERE</c>.
    ///
    /// <para>El <c>switch</c> es de esta superficie —son SUS identificadores de campo y SU
    /// vocabulario de estado— pero los predicados salen de
    /// <see cref="ProcedureInstanceFiltroSql"/>, compartidos con el listado del gestor. Ahí está el
    /// punto: las dos pantallas preguntan sobre la misma tabla, y una placa que casara en una y no
    /// en la otra no daría un error, haría que el producto se contradijera sobre el mismo
    /// trámite.</para>
    ///
    /// <para>Un campo desconocido devuelve la consulta INTACTA en vez de lanzar: el endpoint ya
    /// rechaza con 400 lo que no está en el catálogo, así que llegar aquí con uno significaría que
    /// catálogo y traductor se desincronizaron. Silenciarlo es preferible a tumbar la bandeja, y la
    /// validación de arriba es la que impide que pase inadvertido.</para>
    /// </summary>
    private IQueryable<ProcedureInstance> ApplyCondition(
        IQueryable<ProcedureInstance> query, QueryCondition condicion)
    {
        var op = condicion.Operator;
        var esIdentificador = OtBandejaQueryFieldCatalog.IsIdentifier(condicion.FieldId);
        var valores = ProcedureInstanceFiltroSql.Normalizar(condicion, esIdentificador);

        if (ProcedureInstanceFiltroSql.EsInerte(op, valores))
            return query;

        return condicion.FieldId switch
        {
            OtBandejaQueryFieldCatalog.Radicado =>
                ProcedureInstanceFiltroSql.PorRadicado(query, op, valores),
            OtBandejaQueryFieldCatalog.Placa =>
                ProcedureInstanceFiltroSql.PorPlaca(query, op, valores),
            OtBandejaQueryFieldCatalog.Vin =>
                ProcedureInstanceFiltroSql.PorVin(query, op, valores),

            OtBandejaQueryFieldCatalog.Comprador => ProcedureInstanceFiltroSql.PorActor(
                query, op, valores, ProcedureInstanceFiltroSql.ActorTipoComprador),
            OtBandejaQueryFieldCatalog.Vendedor => ProcedureInstanceFiltroSql.PorActor(
                query, op, valores, ProcedureInstanceFiltroSql.ActorTipoVendedor),

            // «Empresa cliente» para el organismo es la misma columna que «Compañía» para el gestor:
            // el tenant dueño del trámite, mirado desde el otro lado.
            OtBandejaQueryFieldCatalog.Empresa =>
                ProcedureInstanceFiltroSql.PorTenant(query, op, valores),
            // Por Id y NO por el código del tipo, que es como lo compara el listado del gestor: es
            // la convención de la superficie OT —su catálogo de Consultas ya nombra los tipos por
            // Id— y el filtro suelto `procedureTypeId` de esta misma bandeja también. Es la clase de
            // diferencia que no se puede compartir: el predicado compartido compara otra columna.
            OtBandejaQueryFieldCatalog.TipoTramite => PorTipoTramiteId(query, op, valores),

            // El estado CRUDO que el organismo recibe. No es la lectura derivada del informe: ver la
            // justificación en OtBandejaQueryFieldCatalog.
            OtBandejaQueryFieldCatalog.Estado => op switch
            {
                QueryOperator.EsAlguno => query.Where(x => valores.Contains(x.Status.ToUpper())),
                QueryOperator.NoEsNinguno => query.Where(x => !valores.Contains(x.Status.ToUpper())),
                _ => query,
            },

            OtBandejaQueryFieldCatalog.SubEstadoPlaca => PorSubEstadoPlaca(query, op, valores),

            // Gestor = quien radicó el trámite en la empresa cliente. Es el mismo criterio que ya
            // usaba el filtro suelto de la bandeja: para el organismo, el interlocutor es quien
            // radicó, no a quién se lo reasignaron puertas adentro de la empresa.
            OtBandejaQueryFieldCatalog.Gestor => op switch
            {
                QueryOperator.EsAlguno => query.Where(x => _context.Users.Any(u =>
                    u.Id == x.CreatedByUserId && valores.Contains(u.DisplayName.ToUpper()))),
                QueryOperator.NoEsNinguno => query.Where(x => !_context.Users.Any(u =>
                    u.Id == x.CreatedByUserId && valores.Contains(u.DisplayName.ToUpper()))),
                QueryOperator.Contiene => query.Where(x => _context.Users.Any(u =>
                    u.Id == x.CreatedByUserId && u.DisplayName.ToUpper().Contains(valores[0]))),
                QueryOperator.EstaVacio => query.Where(x =>
                    !_context.Users.Any(u => u.Id == x.CreatedByUserId)),
                QueryOperator.NoEstaVacio => query.Where(x =>
                    _context.Users.Any(u => u.Id == x.CreatedByUserId)),
                _ => query,
            },

            OtBandejaQueryFieldCatalog.Prioritario => ProcedureInstanceFiltroSql.Booleano(
                query, op, valores,
                verdadero: q => q.Where(x => x.Prioritario),
                falso: q => q.Where(x => !x.Prioritario)),

            OtBandejaQueryFieldCatalog.Prenda => ProcedureInstanceFiltroSql.Booleano(
                query, op, valores,
                verdadero: q => q.Where(ProcedureInstanceFiltroSql.TienePrenda(_context)),
                falso: q => q.Where(ProcedureInstanceFiltroSql.Negar(
                    ProcedureInstanceFiltroSql.TienePrenda(_context)))),

            _ => query,
        };
    }

    /// <summary>
    /// Tipo de trámite por identificador. Un valor que no sea un Guid se descarta en vez de tumbar
    /// la consulta; si no queda ninguno, la condición no acota (filtrar por «nada» no debe vaciar
    /// la bandeja).
    /// </summary>
    private static IQueryable<ProcedureInstance> PorTipoTramiteId(
        IQueryable<ProcedureInstance> query, string op, List<string> valores)
    {
        var ids = new List<Guid>();
        foreach (var valor in valores)
            if (Guid.TryParse(valor, out var id)) ids.Add(id);
        if (ids.Count == 0) return query;

        return op switch
        {
            QueryOperator.EsAlguno => query.Where(p => ids.Contains(p.ProcedureTypeId)),
            QueryOperator.NoEsNinguno => query.Where(p => !ids.Contains(p.ProcedureTypeId)),
            _ => query,
        };
    }

    /// <summary>
    /// Ruta de placa. <c>sin_ruta</c> no es un valor de la columna sino su ausencia, así que se trata
    /// aparte para poder pedirlo junto a valores reales — misma regla que ya aplica el filtro suelto
    /// <c>PlateFlowStatus</c> que mandan las tarjetas de la cabecera.
    /// </summary>
    private static IQueryable<ProcedureInstance> PorSubEstadoPlaca(
        IQueryable<ProcedureInstance> query, string op, List<string> valores)
    {
        var enMinuscula = valores.Select(v => v.ToLowerInvariant()).ToList();
        var incluirSinRuta = enMinuscula.Remove(PlateFlowSinRuta);
        var negado = op == QueryOperator.NoEsNinguno;

        if (op != QueryOperator.EsAlguno && !negado)
            return query;

        // Se arma el predicado en positivo y se niega al final: escribir las dos ramas por separado
        // es la forma de que «es alguno» y «no es ninguno» dejen de ser complementarios sin que
        // nadie lo note.
        return (incluirSinRuta, enMinuscula.Count > 0, negado) switch
        {
            (true, true, false) => query.Where(p =>
                p.PlateFlowStatus == null || enMinuscula.Contains(p.PlateFlowStatus)),
            (true, true, true) => query.Where(p =>
                p.PlateFlowStatus != null && !enMinuscula.Contains(p.PlateFlowStatus)),
            (true, false, false) => query.Where(p => p.PlateFlowStatus == null),
            (true, false, true) => query.Where(p => p.PlateFlowStatus != null),
            (false, true, false) => query.Where(p =>
                p.PlateFlowStatus != null && enMinuscula.Contains(p.PlateFlowStatus)),
            (false, true, true) => query.Where(p =>
                p.PlateFlowStatus == null || !enMinuscula.Contains(p.PlateFlowStatus)),
            _ => query,
        };
    }

    /// <summary>
    /// Prioritario siempre primero (HU #10536). Luego la columna pedida; si no hay SortBy válido,
    /// CreatedAt DESC. Empate estable por Id DESC.
    /// </summary>
    private IOrderedQueryable<ProcedureInstance> ApplyListSort(
        IQueryable<ProcedureInstance> query,
        OtClientProcedureFilter filter)
    {
        var asc = string.Equals(filter.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var sortBy = (filter.SortBy ?? string.Empty).Trim().ToLowerInvariant();

        // Prioritario primero siempre; el resto es ThenBy según la columna elegida.
        var ordered = query.OrderByDescending(p => p.Prioritario);

        return (sortBy, asc) switch
        {
            ("vin", true) => ordered.ThenBy(p => p.Vin).ThenByDescending(p => p.Id),
            ("vin", false) => ordered.ThenByDescending(p => p.Vin).ThenByDescending(p => p.Id),
            ("placa", true) => ordered.ThenBy(p => p.Plate).ThenByDescending(p => p.Id),
            ("placa", false) => ordered.ThenByDescending(p => p.Plate).ThenByDescending(p => p.Id),
            ("vendedor", true) => ordered.ThenBy(p => p.VendedorNombre).ThenByDescending(p => p.Id),
            ("vendedor", false) => ordered.ThenByDescending(p => p.VendedorNombre).ThenByDescending(p => p.Id),
            ("comprador", true) => ordered.ThenBy(p => p.CompradorNombre).ThenByDescending(p => p.Id),
            ("comprador", false) => ordered.ThenByDescending(p => p.CompradorNombre).ThenByDescending(p => p.Id),
            ("gestor", true) => ordered
                .ThenBy(p => _context.Users
                    .Where(u => u.Id == p.CreatedByUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault())
                .ThenByDescending(p => p.Id),
            ("gestor", false) => ordered
                .ThenByDescending(p => _context.Users
                    .Where(u => u.Id == p.CreatedByUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault())
                .ThenByDescending(p => p.Id),
            // HU #12371 — por la parte numérica, no por el texto: ordenar FT1-…/FT2-… como texto
            // agruparía por familia. Misma regla que el listado del gestor.
            ("referencenumber", true) or ("radicado", true) =>
                ordered.ThenBy(p => p.Consecutivo).ThenByDescending(p => p.Id),
            ("referencenumber", false) or ("radicado", false) =>
                ordered.ThenByDescending(p => p.Consecutivo).ThenByDescending(p => p.Id),
            ("status", true) or ("estado", true) =>
                ordered.ThenBy(p => p.Status).ThenByDescending(p => p.Id),
            ("status", false) or ("estado", false) =>
                ordered.ThenByDescending(p => p.Status).ThenByDescending(p => p.Id),
            // La celda «Empresa / Gestor» apila los dos datos y hasta la HU #12219 solo se podía
            // ordenar por el segundo: la cabecera prometía un orden por empresa que no existía. La
            // razón social vive en otra tabla, así que va por subconsulta correlacionada — el mismo
            // patrón que ya usaba «gestor» contra identity.users.
            ("empresa", true) => ordered
                .ThenBy(p => _context.Tenants
                    .Where(t => t.Id == p.TenantId)
                    .Select(t => t.LegalName)
                    .FirstOrDefault())
                .ThenByDescending(p => p.Id),
            ("empresa", false) => ordered
                .ThenByDescending(p => _context.Tenants
                    .Where(t => t.Id == p.TenantId)
                    .Select(t => t.LegalName)
                    .FirstOrDefault())
                .ThenByDescending(p => p.Id),
            ("tipo_tramite", true) or ("tipotramite", true) => ordered
                .ThenBy(p => p.ProcedureType != null ? p.ProcedureType.Name : "")
                .ThenByDescending(p => p.Id),
            ("tipo_tramite", false) or ("tipotramite", false) => ordered
                .ThenByDescending(p => p.ProcedureType != null ? p.ProcedureType.Name : "")
                .ThenByDescending(p => p.Id),
            ("createdat", true) or ("fecharadicacion", true) =>
                ordered.ThenBy(p => p.CreatedAt).ThenByDescending(p => p.Id),
            _ => ordered.ThenByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        };
    }

    private static OtClientProcedure Map(ProcedureInstance entity) => new()
    {
        Id = entity.Id,
        ClientTenantId = entity.TenantId,
        ProcedureTypeId = entity.ProcedureTypeId,
        ReferenceNumber = entity.ReferenceNumber,
        Status = entity.Status,
        Familia = entity.ProcedureType != null ? entity.ProcedureType.Family : "",
        PlateFlowStatus = entity.PlateFlowStatus,
        PlateAssignedAt = entity.PlateAssignedAt,
        PlateUpdatedAt = entity.PlateUpdatedAt,
        TransitOfficeId = entity.TransitOfficeId,
        CreatedAt = entity.CreatedAt,
        SubmittedAt = entity.SubmittedAt,
        Prioritario = entity.Prioritario,
        Placa = entity.Plate,
        Vin = entity.Vin,
        VendedorNombre = entity.VendedorNombre,
        CompradorNombre = entity.CompradorNombre,
    };

    private async Task<IReadOnlyList<OtClientProcedure>> EnrichDisplayNamesAsync(
        List<OtClientProcedure> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var typeIds = items.Select(i => i.ProcedureTypeId).Distinct().ToList();
        var tenantIds = items.Select(i => i.ClientTenantId).Distinct().ToList();

        var typeNames = await _context.ProcedureTypes
            .AsNoTracking()
            .Where(pt => typeIds.Contains(pt.Id))
            .ToDictionaryAsync(pt => pt.Id, pt => pt.Name, cancellationToken)
            .ConfigureAwait(false);

        var tenantNames = await _context.Tenants
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.LegalName, cancellationToken)
            .ConfigureAwait(false);

        // `with` en vez de un clon a mano: la copia campo a campo descartaba en silencio cualquier
        // propiedad nueva del detalle (así se perdieron las especificaciones del vehículo al añadirlas),
        // y el único cambio real aquí son los dos nombres visibles.
        return items
            .Select(item => item with
            {
                ClientTenantName = tenantNames.GetValueOrDefault(item.ClientTenantId, "\u2014"),
                ProcedureTypeName = typeNames.GetValueOrDefault(item.ProcedureTypeId, "\u2014"),
            })
            .ToList();
    }
}

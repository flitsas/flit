using System.Text.Json;
using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.PlatePreassign;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Flit.Tramites.Domain.Tramites.Estados;

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
                entity.UpdatedAt = now;
                entity.UpdatedBy = resolvedChangedBy;

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
                var mapped = await BuildAccessibleQuery(transitOfficeId, clientTenantIds)
                    .Where(p => p.Id == procedureInstanceId)
                    .Select(p => new OtClientProcedure
                    {
                        Id = p.Id,
                        ClientTenantId = p.TenantId,
                        ProcedureTypeId = p.ProcedureTypeId,
                        ReferenceNumber = p.ReferenceNumber,
                        Status = p.Status,
                        // La modalidad gobierna qué causales de rechazo aplican: sin ella, el guard
                        // del rechazo descartaría causales válidas por creerlas de otro proceso.
                        Familia = (p.ProcedureType != null ? p.ProcedureType.Family : ""),
                        PlateFlowStatus = p.PlateFlowStatus,
                        // HU #10804 — soat_estado también en el detalle (mismo criterio de visibilidad).
                        SoatEstado = _context.ProcedureInstanceFieldValues
                            .Where(f => f.ProcedureInstanceId == p.Id
                                && f.FieldKey == Flit.Tramites.Domain.Tramites.Services.SoatGate.FieldKey)
                            .Select(f => f.ValueText)
                            .FirstOrDefault(),
                        // HU #10805 — dígito de preferencia también en el detalle.
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
                        Placa = p.Plate,
                        Vin = p.Vin,
                        VendedorNombre = p.VendedorNombre,
                        CompradorNombre = p.CompradorNombre,
                        GestorNombre = _context.Users
                            .Where(u => u.Id == p.CreatedByUserId)
                            .Select(u => u.DisplayName)
                            .FirstOrDefault(),
                        Marca = _context.ProcedureInstanceFieldValues
                            .Where(f => f.ProcedureInstanceId == p.Id && f.FieldKey == "vehicle_brand")
                            .Select(f => f.ValueText)
                            .FirstOrDefault(),
                        Linea = _context.ProcedureInstanceFieldValues
                            .Where(f => f.ProcedureInstanceId == p.Id && f.FieldKey == "vehicle_line")
                            .Select(f => f.ValueText)
                            .FirstOrDefault(),
                        // Bug #11584 — VerifikResultMapper persiste el año bajo "vehicle_year"
                        // (Flit.Tramites.Application/UseCases/Consultations/VerifikResultMapper.cs:182);
                        // "vehicle_model" nunca se escribe en runtime, solo existe como alias legado
                        // documentado en las herramientas de migración (TransferFieldMap/RegistrationFieldMap).
                        // Se conserva como fallback por si hay datos históricos migrados con esa llave.
                        Modelo = _context.ProcedureInstanceFieldValues
                            .Where(f => f.ProcedureInstanceId == p.Id
                                && (f.FieldKey == "vehicle_year" || f.FieldKey == "vehicle_model"))
                            .OrderBy(f => f.FieldKey == "vehicle_year" ? 0 : 1)
                            .Select(f => f.ValueText)
                            .FirstOrDefault(),
                        Color = _context.ProcedureInstanceFieldValues
                            .Where(f => f.ProcedureInstanceId == p.Id && f.FieldKey == "vehicle_color")
                            .Select(f => f.ValueText)
                            .FirstOrDefault(),
                        Clase = _context.ProcedureInstanceFieldValues
                            .Where(f => f.ProcedureInstanceId == p.Id && f.FieldKey == "vehicle_class")
                            .Select(f => f.ValueText)
                            .FirstOrDefault(),
                        Servicio = _context.ProcedureInstanceFieldValues
                            .Where(f => f.ProcedureInstanceId == p.Id && f.FieldKey == "vehicle_service")
                            .Select(f => f.ValueText)
                            .FirstOrDefault(),
                        Combustible = _context.ProcedureInstanceFieldValues
                            .Where(f => f.ProcedureInstanceId == p.Id && f.FieldKey == "vehicle_fuel")
                            .Select(f => f.ValueText)
                            .FirstOrDefault(),
                    })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (mapped is null)
                {
                    return null;
                }

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

                var withActors = new OtClientProcedure
                {
                    Id = mapped.Id,
                    ClientTenantId = mapped.ClientTenantId,
                    ProcedureTypeId = mapped.ProcedureTypeId,
                    ProcedureTypeName = mapped.ProcedureTypeName,
                    ClientTenantName = mapped.ClientTenantName,
                    ReferenceNumber = mapped.ReferenceNumber,
                    Status = mapped.Status,
                    Familia = mapped.Familia,
                    PlateFlowStatus = mapped.PlateFlowStatus,
                    SoatEstado = mapped.SoatEstado,
                    PlatePreferredLastDigit = mapped.PlatePreferredLastDigit,
                    SoatPagado = mapped.SoatPagado,
                    ImpuestoDepartamentalPagado = mapped.ImpuestoDepartamentalPagado,
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
                    Marca = mapped.Marca,
                    Linea = mapped.Linea,
                    Modelo = mapped.Modelo,
                    Color = mapped.Color,
                    Clase = mapped.Clase,
                    Servicio = mapped.Servicio,
                    Combustible = mapped.Combustible,
                };

                var enriched = await EnrichDisplayNamesAsync([withActors], cancellationToken)
                    .ConfigureAwait(false);
                return enriched[0];
            },
            cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<ProcedureInstance> BuildAccessibleQuery(
        Guid transitOfficeId,
        IReadOnlyList<Guid> clientTenantIds) =>
        _context.ProcedureInstances
            .AsNoTracking()
            .Where(p => p.DeletedAt == null
                && p.TransitOfficeId == transitOfficeId
                && clientTenantIds.Contains(p.TenantId));

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

        return query;
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
            ("referencenumber", true) or ("radicado", true) =>
                ordered.ThenBy(p => p.ReferenceNumber).ThenByDescending(p => p.Id),
            ("referencenumber", false) or ("radicado", false) =>
                ordered.ThenByDescending(p => p.ReferenceNumber).ThenByDescending(p => p.Id),
            ("status", true) or ("estado", true) =>
                ordered.ThenBy(p => p.Status).ThenByDescending(p => p.Id),
            ("status", false) or ("estado", false) =>
                ordered.ThenByDescending(p => p.Status).ThenByDescending(p => p.Id),
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

        return items
            .Select(item => new OtClientProcedure
            {
                Id = item.Id,
                ClientTenantId = item.ClientTenantId,
                ClientTenantName = tenantNames.GetValueOrDefault(item.ClientTenantId, "—"),
                ProcedureTypeId = item.ProcedureTypeId,
                ProcedureTypeName = typeNames.GetValueOrDefault(item.ProcedureTypeId, "—"),
                ReferenceNumber = item.ReferenceNumber,
                Status = item.Status,
                Familia = item.Familia,
                PlateFlowStatus = item.PlateFlowStatus,
                SoatEstado = item.SoatEstado,
                PlatePreferredLastDigit = item.PlatePreferredLastDigit,
                SoatPagado = item.SoatPagado,
                ImpuestoDepartamentalPagado = item.ImpuestoDepartamentalPagado,
                TransitOfficeId = item.TransitOfficeId,
                CreatedAt = item.CreatedAt,
                SubmittedAt = item.SubmittedAt,
                Prioritario = item.Prioritario,
                Actors = item.Actors,
                Placa = item.Placa,
                Vin = item.Vin,
                VendedorNombre = item.VendedorNombre,
                CompradorNombre = item.CompradorNombre,
                GestorNombre = item.GestorNombre,
                Marca = item.Marca,
                Linea = item.Linea,
                Modelo = item.Modelo,
                Color = item.Color,
                Clase = item.Clase,
                Servicio = item.Servicio,
                Combustible = item.Combustible,
            })
            .ToList();
    }
}

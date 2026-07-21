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

                        if (!string.IsNullOrWhiteSpace(filter.Status))
                        {
                            query = query.Where(p => p.Status == filter.Status.Trim());
                        }

                        if (filter.ProcedureTypeId is not null)
                        {
                            query = query.Where(p => p.ProcedureTypeId == filter.ProcedureTypeId.Value);
                        }

                        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
                        if (totalCount == 0)
                        {
                            return PagedResult<OtClientProcedure>.Empty;
                        }

                        var items = await query
                            // HU #10536 — los trámites prioritarios se revisan con primacía en la bandeja del OT.
                            .OrderByDescending(p => p.Prioritario)
                            .ThenByDescending(p => p.CreatedAt)
                            .ThenByDescending(p => p.Id)
                            .Skip((filter.Page - 1) * filter.PageSize)
                            .Take(filter.PageSize)
                            .Select(p => new OtClientProcedure
                            {
                                Id = p.Id,
                                ClientTenantId = p.TenantId,
                                ProcedureTypeId = p.ProcedureTypeId,
                                ReferenceNumber = p.ReferenceNumber,
                                Status = p.Status,
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
                                TransitOfficeId = p.TransitOfficeId,
                                CreatedAt = p.CreatedAt,
                                SubmittedAt = p.SubmittedAt,
                                Prioritario = p.Prioritario,
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
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            otTenantId,
            procedureInstanceId,
            TramiteEstado.Aprobado,
            approvedBy,
            reason: null,
            source,
            cancellationToken);

    public Task<OtClientProcedure?> RejectAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        Guid? rejectedBy,
        string source,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            otTenantId,
            procedureInstanceId,
            TramiteEstado.Rechazado,
            rejectedBy,
            reason,
            source,
            cancellationToken);

    // La decisión del OT (aprobar/rechazar) aplica SIEMPRE desde 'entregado' (máquina == develop). La ruta
    // de placa no cambia el status: su progreso vive en plate_flow_status (sub-estado interno, HU #10785).
    private async Task<OtClientProcedure?> TransitionAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string targetStatus,
        Guid? changedBy,
        string? reason,
        string source,
        CancellationToken cancellationToken)
    {
        var accessible = await ExecuteOtScopedAsync(
            otTenantId,
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

                // Feature #10587 / HU #10785 — gates de la ruta de placa en la aprobación del OT. El status
                // global es 'entregado' (máquina == develop); el sub-flujo de placa vive en plate_flow_status:
                //  · No se puede aprobar un trámite de la ruta de placa aún en 'preasignado' (sin placa): el
                //    OT debe registrar la placa primero (AssignPlate → asignado).
                //  · Gate DURO de SOAT (R06): con la placa 'asignado', el SOAT debe estar VIGENTE para aprobar
                //    (no subsanable). 'vencido'/'unknown'/null/desconocido BLOQUEAN la aprobación.
                if (targetStatus == TramiteEstado.Aprobado
                    && entity.PlateFlowStatus == PlateFlowStatus.Preasignado)
                {
                    return null;
                }

                if (targetStatus == TramiteEstado.Aprobado
                    && entity.PlateFlowStatus == PlateFlowStatus.Asignado)
                {
                    var soatEstado = await _context.ProcedureInstanceFieldValues
                        .AsNoTracking()
                        .Where(f => f.ProcedureInstanceId == procedureInstanceId
                            && f.FieldKey == Flit.Tramites.Domain.Tramites.Services.SoatGate.FieldKey)
                        .Select(f => f.ValueText)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (Flit.Tramites.Domain.Tramites.Services.SoatGate.BlocksApproval(soatEstado))
                    {
                        return null;
                    }
                }

                var resolvedChangedBy = await ResolveChangedByAsync(changedBy, cancellationToken)
                    .ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                entity.Status = targetStatus;
                entity.UpdatedAt = now;
                entity.UpdatedBy = resolvedChangedBy;

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

                // Feature #10701 — la decisión del OT (aprobar/rechazar) invalida el consolidado
                // maestro persistido: el próximo "Ver consolidado" lo regenerará antes de mostrarlo.
                entity.ConsolidadoMaestroVigente = false;

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

                // El historial se escribe aquí (no vía ITramiteTransitionRecorder) para conservar
                // el metadata cross-tenant (ot_tenant_id/source) dentro de la transacción RLS del
                // tenant cliente; la unificación con el recorder queda para la integración N 03.
                _context.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    TenantId = accessible.ClientTenantId,
                    ProcedureInstanceId = entity.Id,
                    FromStatus = effectiveFrom,
                    ToStatus = targetStatus,
                    ChangedAt = now,
                    ChangedBy = resolvedChangedBy,
                    Reason = reason,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        ot_tenant_id = otTenantId,
                        approver_tenant_id = otTenantId,
                        source,
                    }),
                });

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var mapped = Map(entity);
                var enriched = await EnrichDisplayNamesAsync([mapped], cancellationToken)
                    .ConfigureAwait(false);
                return enriched[0];
            },
            cancellationToken).ConfigureAwait(false);
    }

    // HU #10654 (Feature #10587 / HU #10785) — el OT asigna una placa a un trámite de la ruta de placa en
    // sub-estado 'preasignado' (Flujo B): reserva la placa, la escribe en field_values (el trigger lo
    // permite con plate_flow_status='preasignado') y avanza el SUB-ESTADO preasignado→asignado. El status
    // global permanece en 'entregado' (no hay transición de la máquina de estados).
    public async Task<OtClientProcedure?> AssignPlateAsync(
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
            return null;
        }

        var accessible = await ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeId => FindAccessibleProcedureAsync(transitOfficeId, procedureInstanceId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (accessible is null || accessible.TransitOfficeId is not { } officeId)
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
                    || entity.PlateFlowStatus != PlateFlowStatus.Preasignado)
                {
                    return null;
                }

                // HU #10800 — Flujo B: el OT elige una placa del rango (TryReserve, solo placas disponibles)
                // o registra una placa FUERA DE RANGO (ReserveOutOfRange, la crea como rango ad-hoc de 1 placa).
                var reserved = outOfRange
                    ? (await _plateRepo
                        .ReserveOutOfRangePlateAsync(accessible.ClientTenantId, officeId, plate, procedureInstanceId, cancellationToken)
                        .ConfigureAwait(false)).Success
                    : await _plateRepo
                        .TryReservePlateAsync(accessible.ClientTenantId, officeId, plate, procedureInstanceId, cancellationToken)
                        .ConfigureAwait(false);
                if (!reserved)
                {
                    return null;
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
                return enriched[0];
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
                        TransitOfficeId = p.TransitOfficeId,
                        CreatedAt = p.CreatedAt,
                        SubmittedAt = p.SubmittedAt,
                    })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (mapped is null)
                {
                    return null;
                }

                var enriched = await EnrichDisplayNamesAsync([mapped], cancellationToken)
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

    private static OtClientProcedure Map(ProcedureInstance entity) => new()
    {
        Id = entity.Id,
        ClientTenantId = entity.TenantId,
        ProcedureTypeId = entity.ProcedureTypeId,
        ReferenceNumber = entity.ReferenceNumber,
        Status = entity.Status,
        PlateFlowStatus = entity.PlateFlowStatus,
        TransitOfficeId = entity.TransitOfficeId,
        CreatedAt = entity.CreatedAt,
        SubmittedAt = entity.SubmittedAt,
        Prioritario = entity.Prioritario,
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
                PlateFlowStatus = item.PlateFlowStatus,
                SoatEstado = item.SoatEstado,
                PlatePreferredLastDigit = item.PlatePreferredLastDigit,
                TransitOfficeId = item.TransitOfficeId,
                CreatedAt = item.CreatedAt,
                SubmittedAt = item.SubmittedAt,
                Prioritario = item.Prioritario,
            })
            .ToList();
    }
}

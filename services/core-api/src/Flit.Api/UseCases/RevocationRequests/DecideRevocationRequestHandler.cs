using Flit.Admin.Application.OtClientProcedures;
using Flit.Admin.Application.OtClientProcedures.RevokeOtClientProcedure;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.RevocationRequests;
using Microsoft.Extensions.Logging;

namespace Flit.Api.UseCases.RevocationRequests;

/// <summary>
/// Body de POST .../client-procedures/{id}/revocation-requests/approve|reject (HU #12576, Feature #12565).
/// </summary>
public sealed class DecideRevocationRequestApiRequest
{
    /// <summary>
    /// Aprobar: motivo OPCIONAL (auditoría, mismo criterio que <see cref="RevokeOtClientProcedureCommand"/>
    /// de HU #12166). Rechazar: OBLIGATORIO (AC2 — 422 <c>motivo_requerido</c> si falta).
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>«Decidir una solicitud de revocatoria» (HU #12576, Feature #12565).</summary>
public sealed record DecideRevocationRequestCommand(
    Guid OtTenantId,
    Guid ProcedureInstanceId,
    bool Approve,
    string? Reason,
    Guid? DecidedBy,
    Guid? TransitOfficeId);

public enum DecideRevocationRequestStatus
{
    Approved,
    Rejected,
    ProcedureNotFound,
    RequestNotFound,
    InvalidState,
    QuipuxReadOnly,
    MotivoRequerido,
}

public sealed class DecideRevocationRequestResult
{
    public DecideRevocationRequestStatus Status { get; init; }

    /// <summary>Solo en <see cref="DecideRevocationRequestStatus.Approved"/> (trámite ya Revocado).</summary>
    public OtClientProcedureResponse? Procedure { get; init; }

    public Guid? RevocationRequestId { get; init; }
    public int? AttemptNumber { get; init; }
    public string? RequestStatus { get; init; }

    public static DecideRevocationRequestResult Approved(
        OtClientProcedureResponse procedure, ProcedureRevocationRequest request) => new()
    {
        Status = DecideRevocationRequestStatus.Approved,
        Procedure = procedure,
        RevocationRequestId = request.Id,
        AttemptNumber = request.AttemptNumber,
        RequestStatus = request.Status,
    };

    public static DecideRevocationRequestResult Rejected(ProcedureRevocationRequest request) => new()
    {
        Status = DecideRevocationRequestStatus.Rejected,
        RevocationRequestId = request.Id,
        AttemptNumber = request.AttemptNumber,
        RequestStatus = request.Status,
    };

    public static DecideRevocationRequestResult ProcedureNotFound() =>
        new() { Status = DecideRevocationRequestStatus.ProcedureNotFound };

    public static DecideRevocationRequestResult RequestNotFound() =>
        new() { Status = DecideRevocationRequestStatus.RequestNotFound };

    public static DecideRevocationRequestResult InvalidState() =>
        new() { Status = DecideRevocationRequestStatus.InvalidState };

    public static DecideRevocationRequestResult QuipuxReadOnly() =>
        new() { Status = DecideRevocationRequestStatus.QuipuxReadOnly };

    public static DecideRevocationRequestResult MotivoRequerido() =>
        new() { Status = DecideRevocationRequestStatus.MotivoRequerido };
}

/// <summary>
/// «Decidir una solicitud de revocatoria» (HU #12576, Feature #12565): el OT aprueba o rechaza la
/// solicitud ACTIVA (<c>solicitada</c>/<c>en_revision</c>, AC1/AC2 — ambas cuentan como "En revisión
/// OT" porque ningún AC de esta HU ni de HU #12571 define un paso explícito de "tomar a revisión") de
/// un trámite Aprobado.
///
/// <para>
/// <b>Bounded contexts:</b> vive en <c>Flit.Api</c> (no en <c>Flit.Admin.Application</c> ni en
/// <c>Flit.Tramites.Application</c>) porque orquesta entre los dos módulos y ninguno de los dos puede
/// referenciar al otro (mismo patrón YA usado por <c>AdminOtEndpoints.ApproveClientProcedureAsync</c>,
/// que compone <c>MandatoApprovalHandler</c> de Trámites con <c>ApproveOtClientProcedureHandler</c> de
/// Admin directamente en el endpoint). Aquí se extrae a una clase inyectable —en vez de inlinearlo en
/// el endpoint— para poder testear el WIRING con NSubstitute sin un <c>WebApplicationFactory</c>.
/// </para>
///
/// <para>
/// <b>AC1 — aprobar:</b> reutiliza ÍNTEGRO <see cref="RevokeOtClientProcedureHandler"/> (HU #12166:
/// guard Quipux + validación de estado + transición Aprobado→Revocado con liberación de placa y
/// documentos históricos) — NO se reimplementa esa lógica. Tras la revocación exitosa, la fila de la
/// solicitud pasa a <see cref="ProcedureRevocationRequestStatus.Aprobada"/> en la MISMA fila (nunca una
/// fila nueva, ver <c>ProcedureRevocationRequest</c>).
/// </para>
///
/// <para>
/// <b>AC2 — rechazar:</b> motivo OBLIGATORIO (422, código <c>motivo_requerido</c> — MISMO literal que
/// el resto del módulo Trámites, <c>TramiteEstadoErrores.MotivoRequerido</c>). El trámite NO cambia —
/// permanece Aprobado (ADR-0022,
/// esta HU no toca <c>TramiteStateMachine</c>). El gestor puede reintentar: <c>RequestRevocationHandler</c>
/// (HU #12572) ya trata una fila <c>rechazada</c> como NO activa, así que el siguiente intento simplemente
/// pasa <see cref="RevocationRequestGate"/> de nuevo (ventana vigente desde la aprobación ORIGINAL, sin
/// límite de intentos).
/// </para>
///
/// <para>
/// <b>Este endpoint es la ÚNICA vía habilitada</b> (desde esta HU en adelante) para llegar a
/// Aprobado→Revocado a través del sub-flujo de solicitud del Feature #12565. El botón libre de HU #12166
/// (acción unilateral del OT, sin solicitud previa) NO se toca aquí — su retiro es responsabilidad de la
/// Feature #12566 (HUs #12580/#12581), fuera del alcance de esta HU.
/// </para>
///
/// <para>
/// <b>AC3 — autorización:</b> la resuelve la policy del endpoint (<c>AdminAuthorization.OtModulePolicy</c>,
/// MISMA que el resto de <c>/api/v1/admin/ot/*</c>), no este handler.
/// </para>
///
/// <para>
/// <b>AC4 — notificación:</b> best-effort DESPUÉS de persistir la decisión, mismo criterio que
/// <c>RequestRevocationHandler</c> (ADR-0046 Opción B): un fallo de notificación nunca revierte la
/// decisión ya persistida.
/// </para>
/// </summary>
public sealed class DecideRevocationRequestHandler(
    IOtClientProcedureRepository otRepository,
    RevokeOtClientProcedureHandler revokeHandler,
    IProcedureRevocationRequestRepository revocationRepo,
    // Feature #12565 — solo para encolar el evento de tracking (AddEventAsync); comparte el mismo
    // FlitDbContext que revocationRepo (ambos Scoped por request), así que lo que se encola aquí se
    // persiste con el MISMO SaveChangesAsync de abajo, dentro del mismo scope RLS del tenant cliente.
    IProcedureInstanceRepository instanceRepo,
    IRevocationRequestNotifier notifier,
    ILogger<DecideRevocationRequestHandler> logger)
{
    /// <summary>Tipo de evento de tracking cuando el OT aprueba (HU #12577, Feature #12565).</summary>
    public const string EventoTipoAprobada = "revocatoria_aprobada";

    /// <summary>Tipo de evento de tracking cuando el OT rechaza (HU #12577, Feature #12565).</summary>
    public const string EventoTipoRechazada = "revocatoria_rechazada";


    public async Task<DecideRevocationRequestResult> HandleAsync(
        DecideRevocationRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // AC2 — fail-fast: rechazar sin motivo no debe ni resolver acceso ni tocar BD (mismo estilo
        // que RequestRevocationHandler: lo estructural se valida antes del IO).
        if (!command.Approve && string.IsNullOrWhiteSpace(command.Reason))
        {
            return DecideRevocationRequestResult.MotivoRequerido();
        }

        // Acceso cross-tenant del OT al trámite del cliente (grant vigente + organismo, con el mismo
        // override de SuperAdmin que approve/reject/revoke). Da el tenant CLIENTE dueño de la solicitud.
        var access = await otRepository
            .GetByIdAsync(command.OtTenantId, command.ProcedureInstanceId, command.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);
        if (access is null)
        {
            return DecideRevocationRequestResult.ProcedureNotFound();
        }

        // La solicitud a decidir es la ACTIVA del trámite (solicitada|en_revision): el {id} de la ruta
        // es el TRÁMITE, no un id de solicitud suelto — así el OT nunca puede decidir por accidente
        // (o por una carga de UI obsoleta) sobre un intento que ya fue resuelto. Lectura cross-tenant:
        // el session RLS del OT autenticado está en SU tenant, no en el del cliente.
        var activeRequest = await otRepository.ExecuteInClientTenantScopeAsync(
            access.ClientTenantId,
            () => revocationRepo.FindActiveAsync(access.ClientTenantId, command.ProcedureInstanceId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (activeRequest is null)
        {
            return DecideRevocationRequestResult.RequestNotFound();
        }

        return command.Approve
            ? await ApproveAsync(command, access.ClientTenantId, activeRequest, cancellationToken).ConfigureAwait(false)
            : await RejectAsync(command, access.ClientTenantId, activeRequest, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DecideRevocationRequestResult> ApproveAsync(
        DecideRevocationRequestCommand command,
        Guid clientTenantId,
        ProcedureRevocationRequest activeRequest,
        CancellationToken cancellationToken)
    {
        // AC1 — la transición real Aprobado→Revocado (libera placa, marca documentos históricos) la
        // hace ÍNTEGRAMENTE el handler de HU #12166: guard Quipux + revalidación de estado + RevokeAsync
        // en el scope RLS del cliente (su propia transacción, separada de la de abajo).
        var revoked = await revokeHandler.HandleAsync(new RevokeOtClientProcedureCommand
        {
            OtTenantId = command.OtTenantId,
            ProcedureInstanceId = command.ProcedureInstanceId,
            RevokedBy = command.DecidedBy,
            Reason = command.Reason,
            TransitOfficeId = command.TransitOfficeId,
        }, cancellationToken).ConfigureAwait(false);

        switch (revoked.Status)
        {
            case RevokeOtClientProcedureStatus.NotFound:
                return DecideRevocationRequestResult.ProcedureNotFound();
            case RevokeOtClientProcedureStatus.InvalidState:
                // El trámite ya no estaba 'aprobado' (carrera / doble decisión). La solicitud NO se
                // marca Aprobada: quedaría mintiendo sobre una revocación que no ocurrió.
                return DecideRevocationRequestResult.InvalidState();
            case RevokeOtClientProcedureStatus.QuipuxReadOnly:
                return DecideRevocationRequestResult.QuipuxReadOnly();
        }

        var decidedAt = DateTimeOffset.UtcNow;
        await otRepository.ExecuteInClientTenantScopeAsync(clientTenantId, async () =>
        {
            activeRequest.Status = ProcedureRevocationRequestStatus.Aprobada;
            activeRequest.DecidedBy = command.DecidedBy;
            activeRequest.DecidedAt = decidedAt;
            activeRequest.DecisionReason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
            // Feature #12565 — evento PROPIO de bitácora (ver comentario del constructor): sin él, el
            // tracking del trámite solo mostraba la transición genérica "Revocado desde Aprobado" del
            // historial de estados, sin el motivo (opcional) que el OT haya dejado al aprobar.
            await instanceRepo.AddEventAsync(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = clientTenantId,
                ProcedureInstanceId = command.ProcedureInstanceId,
                Tipo = EventoTipoAprobada,
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    revocation_request_id = activeRequest.Id,
                    attempt_number = activeRequest.AttemptNumber,
                    decision_reason = activeRequest.DecisionReason,
                }),
                CreatedAt = decidedAt,
                CreatedBy = command.DecidedBy,
            }, cancellationToken).ConfigureAwait(false);
            await revocationRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        await NotifyBestEffortAsync(clientTenantId, activeRequest, approved: true, cancellationToken).ConfigureAwait(false);

        return DecideRevocationRequestResult.Approved(revoked.Procedure!, activeRequest);
    }

    private async Task<DecideRevocationRequestResult> RejectAsync(
        DecideRevocationRequestCommand command,
        Guid clientTenantId,
        ProcedureRevocationRequest activeRequest,
        CancellationToken cancellationToken)
    {
        var decidedAt = DateTimeOffset.UtcNow;
        await otRepository.ExecuteInClientTenantScopeAsync(clientTenantId, async () =>
        {
            activeRequest.Status = ProcedureRevocationRequestStatus.Rechazada;
            activeRequest.DecidedBy = command.DecidedBy;
            activeRequest.DecidedAt = decidedAt;
            // No-null garantizado por el fail-fast al inicio de HandleAsync.
            activeRequest.DecisionReason = command.Reason!.Trim();
            // Feature #12565 — evento PROPIO de bitácora (ver comentario del constructor). Sin esto,
            // un rechazo no dejaba NINGÚN rastro en el tracking: el trámite permanece Aprobado (ADR-0022
            // no toca TramiteStateMachine) y por tanto tampoco genera una fila de historial de estados.
            await instanceRepo.AddEventAsync(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = clientTenantId,
                ProcedureInstanceId = command.ProcedureInstanceId,
                Tipo = EventoTipoRechazada,
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    revocation_request_id = activeRequest.Id,
                    attempt_number = activeRequest.AttemptNumber,
                    decision_reason = activeRequest.DecisionReason,
                }),
                CreatedAt = decidedAt,
                CreatedBy = command.DecidedBy,
            }, cancellationToken).ConfigureAwait(false);
            await revocationRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        await NotifyBestEffortAsync(clientTenantId, activeRequest, approved: false, cancellationToken).ConfigureAwait(false);

        return DecideRevocationRequestResult.Rejected(activeRequest);
    }

    /// <summary>AC4 — best-effort, DESPUÉS de persistir la decisión (ADR-0046 Opción B).</summary>
    private async Task NotifyBestEffortAsync(
        Guid clientTenantId,
        ProcedureRevocationRequest request,
        bool approved,
        CancellationToken cancellationToken)
    {
        try
        {
            await notifier.NotifyDecisionAsync(
                new RevocationRequestDecidedEvent(
                    clientTenantId,
                    request.ProcedureInstanceId,
                    request.Id,
                    request.AttemptNumber,
                    approved,
                    request.RequestedBy,
                    request.DecidedBy,
                    request.DecidedAt ?? DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DecideRevocationRequestLog.NotifyFailed(logger, request.Id, ex);
        }
    }
}

internal static partial class DecideRevocationRequestLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Decisión de revocatoria {RevocationRequestId}: falló el encolado de la notificación (best-effort, no revierte la decisión).")]
    public static partial void NotifyFailed(ILogger logger, Guid revocationRequestId, Exception ex);
}

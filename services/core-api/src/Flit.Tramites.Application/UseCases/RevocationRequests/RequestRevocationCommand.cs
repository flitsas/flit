using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.RevocationRequests;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.RevocationRequests;

/// <summary>
/// Documento de soporte de la solicitud (HU #12572, AC1/AC2). El endpoint lo arma desde el
/// <c>IFormFile</c> del multipart, igual que <see cref="UploadAttachmentInput"/> — este record vive
/// aparte (y no reutiliza aquel) porque el mecanismo se dispara con reglas propias (solo PDF, trámite
/// Aprobado, no pasa por <see cref="AttachmentRules.AllowsUploadInState"/>).
/// </summary>
public sealed record RevocationSupportDocumentInput(
    string Filename,
    string Mimetype,
    long SizeBytes,
    Stream Content);

/// <summary>Solicitud de revocatoria de un trámite Aprobado (HU #12572, Feature #12565).</summary>
public sealed record RequestRevocationCommand(
    Guid InstanceId,
    Guid TenantId,
    string? Reason,
    bool ConfirmAccuracy,
    bool ConfirmConsequences,
    RevocationSupportDocumentInput? SupportDocument,
    Guid RequestedByUserId);

/// <summary>Resumen mínimo de la solicitud creada (AC1).</summary>
public sealed record RequestRevocationResult(
    Guid Id,
    Guid ProcedureInstanceId,
    int AttemptNumber,
    string Status,
    DateTimeOffset RequestedAt);

/// <summary>
/// «Solicitar revocatoria» (HU #12572, Feature #12565): recibe motivo + documento de soporte + los
/// dos checks de confirmación (AC1, UI en HU #12574) de un Administrador de compañía sobre un
/// trámite <see cref="TramiteEstado.Aprobado"/>, aplica <see cref="RevocationRequestGate"/> (AC1-AC5
/// de HU #12571: origen FLIT, ventana, unicidad) y crea la fila del intento
/// (<see cref="ProcedureRevocationRequest"/>, HU #12570).
///
/// <para>
/// <b>Los "2 checks" del AC1</b> son responsabilidad de copy/UI (HU #12574): aquí solo se exige que
/// ambos booleanos del contrato lleguen en <c>true</c> — el backend no conoce ni valida el texto que
/// el usuario confirmó, solo que confirmó. Sin precedente de un patrón de "confirmación" distinto en
/// el resto de la API de trámites, se modelan como dos campos booleanos simples del request (forma
/// más directa y sin ambigüedad para el frontend), en vez de un objeto anidado o un array de flags.
/// </para>
///
/// <para>
/// <b>Trámite debe estar Aprobado</b> se valida AQUÍ, antes de invocar el gate: <see cref="RevocationRequestGate.Evaluate"/>
/// lanza <see cref="InvalidOperationException"/> (excepción de programación, no de negocio) si el
/// estado no es <see cref="TramiteEstado.Aprobado"/>, por diseño (ver XML doc de esa clase). El
/// código de error reutiliza <see cref="ConsultNowHandler.NoAprobado"/> — MISMO literal
/// ("tramite_no_aprobado"), mismo significado ("esta acción exige el trámite Aprobado") en dos
/// flujos distintos del módulo; no se referencia la constante del otro handler a propósito, para no
/// acoplar dos casos de uso que no comparten nada más que el código.
/// </para>
///
/// <para>
/// <b>Documento de soporte:</b> reutiliza <see cref="IAttachmentStorage"/> (mismo servicio de
/// subida/validación de PDF que el resto de trámites, tamaño vía <see cref="AttachmentRules.MaxSizeBytes"/>)
/// y persiste una fila de <see cref="ProcedureInstanceAttachment"/> (tipo
/// <see cref="SupportDocumentTipo"/>) referenciada por <c>support_document_id</c> (HU #12570). NO pasa
/// por <see cref="UploadAttachmentHandler"/>/<see cref="AttachmentValidator"/> completos porque esos
/// exigen <see cref="AttachmentRules.AllowsUploadInState"/> (borrador/subsanación), que este trámite
/// Aprobado nunca cumple; aquí solo se valida tamaño y que el MIME sea EXACTAMENTE
/// <c>application/pdf</c> (AC1 — "PDF válido", más estricto que el whitelist general de adjuntos).
/// </para>
///
/// <para>
/// <b>Timeline (AC1):</b> un evento PROPIO de bitácora (<see cref="EventoTipo"/>, patrón de
/// <c>AdminAnularHandler.EventoTipo</c>), SIN tocar <c>procedure_instance_status_history</c>: el
/// <c>status</c> principal del trámite no cambia (permanece Aprobado durante todo el sub-flujo,
/// ADR-0022).
/// </para>
///
/// <para>
/// <b>Notificación (AC3):</b> tras confirmar la creación de la fila, invoca
/// <see cref="IRevocationRequestNotifier"/> envuelto en <c>try/catch</c> — best-effort, mismo
/// criterio de ADR-0046 (Opción B): un fallo de notificación nunca revierte la solicitud ni tumba la
/// respuesta 201.
/// </para>
/// </summary>
public sealed class RequestRevocationHandler(
    IProcedureInstanceRepository instanceRepo,
    IProcedureRevocationRequestRepository revocationRepo,
    IAttachmentStorage attachmentStorage,
    IBusinessDayCalculator businessDayCalculator,
    IRevocationRequestNotifier notifier,
    ILogger<RequestRevocationHandler> logger)
{
    /// <summary>AC2 — 409: el trámite no está Aprobado (mismo literal que <see cref="ConsultNowHandler.NoAprobado"/>).</summary>
    public const string TramiteNoAprobado = "tramite_no_aprobado";

    /// <summary>AC2 — 422: falta el check de exactitud de la información.</summary>
    public const string ConfirmacionExactitudRequerida = "confirmacion_exactitud_requerida";

    /// <summary>AC2 — 422: falta el check de aceptación de las consecuencias de la revocatoria.</summary>
    public const string ConfirmacionConsecuenciasRequerida = "confirmacion_consecuencias_requerida";

    /// <summary>AC2 — 422: falta el documento de soporte.</summary>
    public const string DocumentoRequerido = "documento_requerido";

    /// <summary>AC1/AC2 — 422: el documento de soporte no es un PDF.</summary>
    public const string DocumentoFormatoInvalido = "documento_formato_invalido";

    /// <summary>AC2 — 422: el documento de soporte excede el tamaño máximo permitido.</summary>
    public const string DocumentoMuyGrande = "documento_muy_grande";

    /// <summary>Tipo de documento del soporte de la solicitud (<c>AttachmentRules.ValidTipos</c>).</summary>
    public const string SupportDocumentTipo = "revocatoria_soporte";

    /// <summary>Tipo del evento PROPIO de bitácora (distinto del genérico <c>cambio_estado</c>).</summary>
    public const string EventoTipo = "revocatoria_solicitada";

    public async Task<(RequestRevocationResult? Result, string? Error, string? ErrorDetail)> HandleAsync(
        RequestRevocationCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // AC2 — validación estructural del request, antes de tocar la BD (fail-fast, sin IO).
        if (string.IsNullOrWhiteSpace(command.Reason))
            return (null, TramiteEstadoErrores.MotivoRequerido, "Debe indicar el motivo de la solicitud de revocatoria.");
        if (!command.ConfirmAccuracy)
            return (null, ConfirmacionExactitudRequerida, "Debe confirmar que la información de la solicitud es exacta.");
        if (!command.ConfirmConsequences)
            return (null, ConfirmacionConsecuenciasRequerida, "Debe confirmar que entiende las consecuencias de solicitar la revocatoria.");
        if (command.SupportDocument is null || command.SupportDocument.SizeBytes <= 0)
            return (null, DocumentoRequerido, "Debe adjuntar el documento de soporte (PDF).");
        var supportDocument = command.SupportDocument;
        if (!string.Equals(supportDocument.Mimetype, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return (null, DocumentoFormatoInvalido, "El documento de soporte debe ser un archivo PDF.");
        if (supportDocument.SizeBytes > AttachmentRules.MaxSizeBytes)
            return (null, DocumentoMuyGrande, "El documento de soporte excede el tamaño máximo permitido.");

        var instance = await instanceRepo.GetByIdAsync(command.InstanceId, command.TenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, TramiteEstadoErrores.NoEncontrado, null);

        // "Trámite debe estar Aprobado" — SIEMPRE antes de invocar el gate (ver XML doc de la clase y
        // de RevocationRequestGate.Evaluate: fuera de 'aprobado' lanza excepción de programación).
        if (!string.Equals(instance.Status, TramiteEstado.Aprobado, StringComparison.Ordinal))
            return (null, TramiteNoAprobado,
                $"El trámite está en estado '{instance.Status}': la revocatoria solo aplica a trámites Aprobados.");

        // AC2/AC5 — base fija de la ventana: la aprobación ORIGINAL, no la más reciente.
        var approvedAt = await revocationRepo.GetFirstApprovedAtAsync(command.TenantId, instance.Id, ct).ConfigureAwait(false)
            ?? instance.UpdatedAt ?? instance.CreatedAt;

        int? revocationWindowDays = instance.TransitOfficeId is Guid transitOfficeId
            ? await revocationRepo.GetRevocationWindowBusinessDaysAsync(transitOfficeId, ct).ConfigureAwait(false)
            : null;

        var hasActiveRequest = await revocationRepo.FindActiveAsync(command.TenantId, instance.Id, ct).ConfigureAwait(false) is not null;

        var now = DateTimeOffset.UtcNow;
        var gate = RevocationRequestGate.Evaluate(
            instance.Status,
            instance.Origin,
            instance.IsMigrated,
            approvedAt,
            revocationWindowDays,
            hasActiveRequest,
            now,
            businessDayCalculator);

        if (!gate.Ok)
            return (null, gate.Code, gate.Message);

        var attemptNumber = await revocationRepo.GetNextAttemptNumberAsync(command.TenantId, instance.Id, ct).ConfigureAwait(false);

        // Documento de soporte: mismo servicio de almacenamiento/hash que el resto de adjuntos de
        // trámites (AC1/AC2 punto 6). Se sube DESPUÉS de pasar el gate, para no persistir binarios de
        // solicitudes que de todas formas se van a rechazar (ventana vencida, origen no soportado,
        // solicitud activa existente).
        var stored = await attachmentStorage.SaveAsync(
            instance.Id, SupportDocumentTipo, supportDocument.Filename, supportDocument.Content, ct)
            .ConfigureAwait(false);

        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = SupportDocumentTipo,
            Filename = string.IsNullOrWhiteSpace(supportDocument.Filename) ? "file" : supportDocument.Filename.Trim(),
            Mimetype = supportDocument.Mimetype.Trim().ToLowerInvariant(),
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            StoragePath = stored.StoragePath,
            Source = "user",
            UploadedAt = now,
            UploadedBy = command.RequestedByUserId,
        };
        // PK store-generated (uuidv7) con Id ya seteado: Add() directo sobre el DbSet fuerza Added
        // (mismo comentario que UploadAttachmentHandler — aquí no hay grafo de navegación de por medio,
        // así que basta con la llamada directa).
        instanceRepo.Add(attachment);

        var request = new ProcedureRevocationRequest
        {
            Id = Guid.CreateVersion7(),
            TenantId = command.TenantId,
            ProcedureInstanceId = instance.Id,
            AttemptNumber = attemptNumber,
            Status = ProcedureRevocationRequestStatus.Solicitada,
            Reason = command.Reason.Trim(),
            SupportDocumentId = attachment.Id,
            RequestedBy = command.RequestedByUserId,
            RequestedAt = now,
        };
        revocationRepo.Add(request);

        // Timeline (AC1) — evento PROPIO, SIN tocar procedure_instance_status_history: el status
        // principal del trámite no cambia (ver XML doc de la clase).
        await instanceRepo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = EventoTipo,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                revocation_request_id = request.Id,
                attempt_number = attemptNumber,
                reason = request.Reason,
                support_document_id = attachment.Id,
            }),
            CreatedAt = now,
            CreatedBy = command.RequestedByUserId,
        }, ct).ConfigureAwait(false);

        try
        {
            await revocationRepo.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (ActiveRevocationRequestExistsException)
        {
            // AC4 — carrera concurrente cerrada por el índice único parcial (HU #12570/#12571): mismo
            // código que el gate en memoria habría dado con una lectura sin la carrera.
            return (null, RevocationRequestGate.SolicitudActivaExistente,
                "Ya existe una solicitud de revocatoria activa para este trámite.");
        }

        // AC3 — best-effort, DESPUÉS del commit (ADR-0046 Opción B): un fallo de notificación nunca
        // revierte ni bloquea la respuesta 201 de la solicitud ya persistida.
        try
        {
            await notifier.NotifyAsync(
                new RevocationRequestSolicitadaEvent(
                    command.TenantId, instance.Id, request.Id, attemptNumber, command.RequestedByUserId, now),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RequestRevocationLog.NotifyFailed(logger, request.Id, ex);
        }

        return (new RequestRevocationResult(request.Id, instance.Id, attemptNumber, request.Status, now), null, null);
    }
}

internal static partial class RequestRevocationLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Solicitud de revocatoria {RevocationRequestId}: falló el encolado de la notificación 'solicitud recibida' (best-effort, no revierte la solicitud).")]
    public static partial void NotifyFailed(ILogger logger, Guid revocationRequestId, Exception ex);
}

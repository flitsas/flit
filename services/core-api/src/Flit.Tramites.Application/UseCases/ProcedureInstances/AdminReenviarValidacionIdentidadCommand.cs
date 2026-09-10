using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Orden de reenvío ADMINISTRATIVO de la validación de identidad de un trámite (HU #12161).
/// <see cref="Email"/> es OPCIONAL: si viene vacío/omitido, reenvía al correo actual (AC1); si trae un
/// valor distinto (case-insensitive/trim), se persiste ANTES de reenviar y el envío va al nuevo
/// destinatario (AC2).
/// </summary>
public sealed record AdminReenviarValidacionIdentidadCommand(
    Guid InstanceId,
    Guid ValidationId,
    Guid TenantId,
    string? Email,
    Guid? ChangedByUserId);

/// <summary>Resultado del reenvío administrativo (AC1/AC2/AC4): la validación actualizada + si se encoló.</summary>
public sealed record AdminReenviarValidacionIdentidadResult(
    BiometricValidationDto Validation,
    string CaptureUrl,
    bool EmailActualizado,
    bool Queued);

/// <summary>
/// "Reenviar validación de identidad" (Feature #12155, HU #12161): reenvía la validación de identidad de
/// UNA parte de un trámite fuera del flujo normal del wizard, para destrabar validaciones con datos de
/// contacto incorrectos MIENTRAS la identidad sigue siendo accionable (AC3).
///
/// <para>
/// <b>Por qué NO reutiliza ninguno de los dos mecanismos existentes (investigación previa a esta HU):</b>
/// <list type="bullet">
/// <item><description><c>POST /instances/{id}/biometric</c> (<see cref="IniciarKyverumVerifyHandler"/> /
/// <see cref="IniciarBiometriaHandler"/>) solo INICIA una validación NUEVA y está gateado a
/// <see cref="TramiteEstado.PermiteEdicionDatos"/> (<c>not_draft</c>): únicamente borrador o subsanación
/// activa. Ese gate existe para que los datos del expediente no se editen fuera de borrador/subsanación en
/// el flujo NORMAL del gestor; el admin, en cambio, necesita actuar sobre un trámite YA <c>entregado</c>
/// (u otro estado en curso) que ese gate rechaza por diseño — con razón, para el caso que protege.
/// </description></item>
/// <item><description><c>POST /biometric-validations/{id}/resend</c>
/// (<see cref="ReenviarPrevalidacionHandler"/>) SÍ reenvía + permite cambiar el correo (D8, HU #10943),
/// pero rechaza EXPLÍCITAMENTE (<c>no_editable</c>) cualquier validación con
/// <c>ProcedureInstanceId IS NOT NULL</c>: es el mecanismo de la prevalidación STANDALONE, anclada a
/// <c>Person</c> (HU #10865/ADR-0036) — las validaciones ligadas a trámite NUNCA pueblan
/// <c>PersonId</c>/<c>Person</c>. Relajar ese gate mezclaría dos modelos de datos distintos (persona
/// standalone vs. fila de validación de trámite) en un mismo endpoint público.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Diseño elegido:</b> un handler y endpoint ADMIN nuevos, gateados por el permiso
/// <c>AdminTramiteReenviarValidacion</c> (catálogo HU #12157), que operan directamente sobre la fila
/// <see cref="ProcedureInstanceBiometricValidation"/> (que YA trae <c>Name</c>/<c>DocumentType</c>/
/// <c>DocumentNumber</c>/<c>Email</c> propios, capturados del actor al iniciar la validación — no depende
/// de <c>Person</c>) y REUTILIZAN el núcleo de reenvío ya escrito, <see cref="PrevalidacionResendService"/>
/// (mismo registro, token/enlace nuevo, TTL 24h, tope y cooldown de D10): NO se duplica la llamada al
/// proveedor (Kyverum/mock). La única pieza nueva de esta HU es la regla de negocio de QUÉ estados de
/// trámite bloquean la acción (AC3), que es completamente distinta de <c>not_draft</c> y de la
/// editabilidad standalone.
/// </para>
///
/// <para>
/// AC3 — el trámite en <see cref="TramiteEstado.Aprobado"/>, <see cref="TramiteEstado.Anulado"/> o
/// "revocado" (string; ese estado TODAVÍA NO EXISTE en <see cref="TramiteEstado"/>, lo introducirá la
/// Feature hermana #12156/HU #12165 — mismo criterio de comparación por string que
/// <c>AdminAnularHandler</c>, HU #12160) rechaza el reenvío: en esos tres estados la identidad ya no es
/// accionable (decisión ya tomada por el organismo de tránsito, o trámite descartado). Cualquier otro
/// estado (incluido <c>entregado</c>, el caso central de esta HU) permite el reenvío.
/// </para>
///
/// <para>
/// Trazabilidad (AC1/AC2): además de la bitácora TÉCNICA de <see cref="IIdentityValidationAuditLog"/> (que
/// ya cubre el reenvío standalone, stage <see cref="IdentityValidationAuditStages.Resend"/>, con el correo
/// SIEMPRE enmascarado — Habeas Data), persiste un evento PROPIO <see cref="EventoTipo"/> en
/// <c>ProcedureInstanceEvent</c> (mismo patrón <c>AddEventAsync</c> que <c>AdminAnularHandler</c> /
/// <c>AdminCambiarEstadoHandler</c> de este mismo lote) para que el reenvío administrativo quede visible en
/// el HISTORIAL del trámite que consume el dashboard — la bitácora técnica de identidad no lo es.
/// </para>
/// </summary>
public sealed class AdminReenviarValidacionIdentidadHandler(
    IProcedureInstanceRepository repo,
    IKyverumVerifyClient kyverum,
    BiometricsProviderOptions providerOptions,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events,
    IIdentityValidationAuditLog audit)
{
    /// <summary>Tipo del evento PROPIO de bitácora del trámite (historial visible en el dashboard admin).</summary>
    public const string EventoTipo = "reenvio_validacion_admin";

    /// <summary>
    /// Identidad ya aprobada: no aplica reenvío (revalidar exige iniciar una validación nueva). Mismo
    /// guard D9 del mecanismo standalone (<see cref="ReenviarPrevalidacionHandler"/>); no forma parte del
    /// catálogo <see cref="TramiteEstadoErrores"/> porque no es un error de ESTADO DE TRÁMITE sino de
    /// estado de la VALIDACIÓN DE IDENTIDAD (mismo criterio que ese handler).
    /// </summary>
    public const string IdentidadAprobada = "identidad_aprobada";

    /// <summary>
    /// HU #12165 (Feature #12156) TODAVÍA no agrega <c>Revocado</c> a <see cref="TramiteEstado"/>: se
    /// compara contra este literal mientras tanto (mismo criterio que <c>AdminAnularHandler</c>, ver XML
    /// doc de la clase).
    /// </summary>
    private const string RevocadoPendienteHu12165 = "revocado";

    private readonly PrevalidacionResendService _resend = new(kyverum, providerOptions, secretProtector, events);

    public async Task<(AdminReenviarValidacionIdentidadResult? Result, string? Error, string? ErrorDetail, int? CooldownMinutos)> HandleAsync(
        AdminReenviarValidacionIdentidadCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var instance = await repo.GetByIdAsync(command.InstanceId, command.TenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, TramiteEstadoErrores.NoEncontrado, null, null);

        // AC3 — aprobado/anulado/revocado (string, ver XML doc de la clase): la identidad deja de ser
        // accionable, sin importar de dónde venga la solicitud.
        if (string.Equals(instance.Status, TramiteEstado.Aprobado, StringComparison.Ordinal)
            || string.Equals(instance.Status, TramiteEstado.Anulado, StringComparison.Ordinal)
            || string.Equals(instance.Status, RevocadoPendienteHu12165, StringComparison.OrdinalIgnoreCase))
            return (null, TramiteEstadoErrores.IdentidadReenvioNoDisponible,
                $"El trámite está en estado '{instance.Status}': la validación de identidad ya no es " +
                "accionable (decisión del organismo de tránsito, o trámite descartado).", null);

        // Tenant-scoped y tracked (mismo método que el mecanismo standalone); sirve tanto a validaciones
        // standalone como a las ligadas a trámite (ver XML doc del repositorio).
        var validation = await repo.GetBiometricByIdWithPersonAsync(command.ValidationId, command.TenantId, ct)
            .ConfigureAwait(false);
        if (validation is null || validation.ProcedureInstanceId != instance.Id)
            return (null, TramiteEstadoErrores.NoEncontrado, null, null);

        if (validation.Status == BiometricEstados.Aprobado)
            return (null, IdentidadAprobada, null, null);

        var now = DateTimeOffset.UtcNow;

        // Las validaciones ligadas a trámite NUNCA pueblan PersonId/Person (solo lo hace la prevalidación
        // standalone, HU #10865): el sujeto se arma directamente desde la fila, que ya trae su propia
        // copia de nombre/documento (capturados del actor al iniciar la validación, HU #10688).
        var emailActual = (validation.Email ?? string.Empty).Trim();
        var emailSolicitado = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();
        var emailActualizado = emailSolicitado is not null
            && !string.Equals(emailActual, emailSolicitado, StringComparison.OrdinalIgnoreCase);
        var emailDestino = emailActualizado ? emailSolicitado! : emailActual;

        var subject = new IdentitySubjectStandalone(
            validation.Name, validation.DocumentType, validation.DocumentNumber, emailDestino);

        // D10 (tope/cooldown) + envío real al proveedor: núcleo COMPARTIDO con el reenvío standalone, sin
        // duplicar la integración con Kyverum/mock.
        var (error, queued, cooldownMinutos, captureUrl) =
            await _resend.ResendAsync(command.TenantId, validation, subject, now, ct).ConfigureAwait(false);
        if (error is not null)
            return (null, error, null, cooldownMinutos);

        validation.Name = subject.Nombre;
        validation.Email = emailDestino;

        // Habeas Data — bitácora TÉCNICA del ciclo de identidad. Correo SIEMPRE enmascarado.
        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.Resend,
            IdentityValidationAuditOutcomes.Ok,
            TenantId: command.TenantId,
            ProcedureInstanceId: instance.Id,
            ValidationId: validation.Id,
            PartyRole: validation.PartyRole,
            Message: emailActualizado
                ? "Reenvío administrativo con actualización de correo: "
                    + $"{EditarPrevalidacionHandler.MaskEmail(emailActual)} -> {EditarPrevalidacionHandler.MaskEmail(emailDestino)}."
                : $"Reenvío administrativo al correo actual ({EditarPrevalidacionHandler.MaskEmail(emailDestino)}).",
            Detail: $"correo_destino={EditarPrevalidacionHandler.MaskEmail(emailDestino)}; "
                + $"correo_actualizado={emailActualizado}; reenvios_acumulados={validation.ResendCount}; encolado={queued}"),
            ct).ConfigureAwait(false);

        // Evento PROPIO — historial visible del trámite (dashboard admin, AC1/AC2), distinto de la
        // bitácora técnica de identidad. Correo SIEMPRE enmascarado (Habeas Data), mismo criterio que el
        // resto de este lote de HUs administrativas (#12159/#12160).
        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = EventoTipo,
            Payload = JsonSerializer.Serialize(new
            {
                validation_id = validation.Id,
                party_role = validation.PartyRole,
                email_actualizado = emailActualizado,
                correo_destino = EditarPrevalidacionHandler.MaskEmail(emailDestino),
                encolado = queued,
            }),
            CreatedAt = now,
            CreatedBy = command.ChangedByUserId,
        }, ct).ConfigureAwait(false);

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        var dto = IniciarBiometriaHandler.ToDto(validation, now);
        return (new AdminReenviarValidacionIdentidadResult(dto, captureUrl, emailActualizado, queued), null, null, null);
    }
}

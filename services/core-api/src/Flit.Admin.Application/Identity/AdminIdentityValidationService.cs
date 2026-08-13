using Flit.Admin.Domain.Identity;

namespace Flit.Admin.Application.Identity;

/// <summary>
/// Implementación del bloque de validación de identidad administrativa desacoplada por correo (HU #10907,
/// ADR-0034). AGNÓSTICO del sujeto: no conoce la tabla del sujeto — inicia/reenvía con
/// <see cref="IAdminIdentityValidationProvider"/>, persiste con
/// <see cref="IAdminIdentityValidationRepository"/> y, al aprobar, vincula con
/// <see cref="IAdminIdentitySubjectLinker"/>. El correo/documento del sujeto son PII: no se loguean.
/// </summary>
public sealed class AdminIdentityValidationService(
    IAdminIdentityValidationProvider provider,
    IAdminIdentityValidationRepository repository,
    IAdminIdentitySubjectLinker subjectLinker,
    TimeProvider timeProvider,
    IPersonIdentityLookup? personLookup = null) : IAdminIdentityValidationService
{
    public Task<AdminIdentityValidationResult> SendAsync(
        AdminIdentitySubjectDescriptor subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return StartNewAsync(subject, cancellationToken);
    }

    public async Task<AdminIdentityValidationResult> ResendAsync(
        AdminIdentitySubjectDescriptor subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var now = timeProvider.GetUtcNow();
        var latest = await repository
            .FindLatestBySubjectAsync(subject.TenantId, subject.SubjectType, subject.SubjectRef, cancellationToken)
            .ConfigureAwait(false);

        // Respeta la vigencia: si ya hay una identidad aprobada y vigente, NO se reenvía (se reutiliza).
        // En cualquier otro caso (nunca se hizo, en curso, rechazada, expirada o vencida) SÍ se reenvía —
        // sin la guarda `biometria_activa` del flujo de trámite, que bloquearía una en curso.
        if (latest is not null && latest.EsAprobadaVigente(now))
        {
            return new AdminIdentityValidationResult(latest, Reused: true);
        }

        return await StartNewAsync(subject, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdminIdentityValidationResult> EnsureAsync(
        AdminIdentitySubjectDescriptor subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var now = timeProvider.GetUtcNow();

        // 1) El propio sujeto ya está validado y vigente: nada que enviar.
        var own = await repository
            .FindLatestBySubjectAsync(subject.TenantId, subject.SubjectType, subject.SubjectRef, cancellationToken)
            .ConfigureAwait(false);
        if (own is not null && own.EsAprobadaVigente(now))
        {
            return new AdminIdentityValidationResult(own, Reused: true);
        }

        // 2) La PERSONA ya validó en otro sujeto (p. ej. como representante legal): se apalanca esa
        // identidad anclándola también a este sujeto, sin pedirle otra validación biométrica.
        var byDocument = await repository
            .FindLatestApprovedByDocumentAsync(
                subject.TenantId, subject.DocumentType, subject.DocumentNumber, cancellationToken)
            .ConfigureAwait(false);
        if (byDocument is not null && byDocument.EsAprobadaVigente(now))
        {
            await subjectLinker
                .LinkAsync(
                    subject.TenantId, subject.SubjectType, subject.SubjectRef, byDocument.Id,
                    subject.ActorBy, cancellationToken)
                .ConfigureAwait(false);
            return new AdminIdentityValidationResult(byDocument, Reused: true);
        }

        // 3) Sin identidad vigente por ningún lado: se inicia una nueva (el proveedor manda el correo).
        return await StartNewAsync(subject, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdminIdentityValidationResult?> LinkExistingAsync(
        AdminIdentitySubjectDescriptor subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var now = timeProvider.GetUtcNow();

        // Si el propio sujeto ya la tiene vigente, no hay nada que vincular: se devuelve tal cual.
        var own = await repository
            .FindLatestBySubjectAsync(subject.TenantId, subject.SubjectType, subject.SubjectRef, cancellationToken)
            .ConfigureAwait(false);
        if (own is not null && own.EsAprobadaVigente(now))
        {
            return new AdminIdentityValidationResult(own, Reused: true);
        }

        var byDocument = await repository
            .FindLatestApprovedByDocumentAsync(
                subject.TenantId, subject.DocumentType, subject.DocumentNumber, cancellationToken)
            .ConfigureAwait(false);
        if (byDocument is not null && byDocument.EsAprobadaVigente(now))
        {
            await subjectLinker
                .LinkAsync(
                    subject.TenantId, subject.SubjectType, subject.SubjectRef, byDocument.Id,
                    subject.ActorBy, cancellationToken)
                .ConfigureAwait(false);

            return new AdminIdentityValidationResult(byDocument, Reused: true);
        }

        // HU #11028 — la identidad de la persona casi nunca está en la tabla ADMIN: lo normal es que la
        // haya validado dentro del trámite de una compañía (como comprador o vendedor). Se busca ahí,
        // acotado a las compañías que operan con el organismo, y se ESPEJA como validación
        // administrativa del sujeto conservando su fecha de aprobación, su vencimiento original y su
        // certificado. Espejar (y no apuntar) es necesario porque el gate del mandato consulta las
        // validaciones admin por sujeto; falsear la vigencia sería regalar 30 días nuevos.
        if (personLookup is null || subject.TransitOfficeId is not { } transitOfficeId)
            return null;

        var origen = await personLookup
            .FindVigenteInTransitOfficeAsync(
                transitOfficeId, subject.DocumentType, subject.DocumentNumber, now, cancellationToken)
            .ConfigureAwait(false);
        if (origen is null)
            return null;

        var espejo = AdminIdentityValidation.Rehydrate(
            Guid.NewGuid(),
            subject.TenantId,
            subject.SubjectType,
            subject.SubjectRef,
            subject.DocumentType,
            subject.DocumentNumber,
            subject.Name,
            subject.Email,
            AdminIdentityEstados.Aprobado,
            origen.Provider,
            captureUrl: null,
            kyverumVerificationId: null,
            webhookSecretEncrypted: null,
            providerStatus: "aprobado",
            providerPayload:
                $"{{\"origen\":\"tramite\",\"validacion\":\"{origen.Id}\",\"tenant\":\"{origen.TenantId}\"}}",
            certificateHash: origen.CertificateHash,
            validatedAt: origen.ValidatedAt,
            validUntil: origen.ValidUntil,
            createdAt: now,
            updatedAt: now);

        await repository.AddAsync(espejo, cancellationToken).ConfigureAwait(false);
        await subjectLinker
            .LinkAsync(
                subject.TenantId, subject.SubjectType, subject.SubjectRef, espejo.Id,
                subject.ActorBy, cancellationToken)
            .ConfigureAwait(false);

        return new AdminIdentityValidationResult(espejo, Reused: true);
    }

    public async Task<AdminIdentityValidationResult> SimulateApprovedAsync(
        AdminIdentitySubjectDescriptor subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var now = timeProvider.GetUtcNow();

        // Idempotente: con una identidad ya vigente (real o simulada) no se crea otra.
        var own = await repository
            .FindLatestBySubjectAsync(subject.TenantId, subject.SubjectType, subject.SubjectRef, cancellationToken)
            .ConfigureAwait(false);
        if (own is not null && own.EsAprobadaVigente(now))
        {
            return new AdminIdentityValidationResult(own, Reused: true);
        }

        var validationId = Guid.NewGuid();
        var validation = AdminIdentityValidation.CreateSent(
            subject.TenantId,
            subject.SubjectType,
            subject.SubjectRef,
            subject.DocumentType,
            subject.DocumentNumber,
            subject.Name,
            subject.Email,
            AdminIdentityProviders.Mock,
            captureUrl: null,
            kyverumVerificationId: null,
            webhookSecretEncrypted: null,
            providerStatus: "simulada",
            // Payload explícito: quien audite la fila sabe que NO hubo captura biométrica.
            providerPayload: "{\"mock\":true,\"motivo\":\"validacion simulada para pruebas\"}",
            now,
            validationId);

        // Certificado reconocible a simple vista: el sello del documento dirá MOCK-…, nunca una serie real.
        validation.Approve(now, $"MOCK-{validationId:N}"[..24]);

        await repository.AddAsync(validation, cancellationToken).ConfigureAwait(false);
        await subjectLinker
            .LinkAsync(
                subject.TenantId, subject.SubjectType, subject.SubjectRef, validation.Id,
                subject.ActorBy, cancellationToken)
            .ConfigureAwait(false);

        return new AdminIdentityValidationResult(validation, Reused: false);
    }

    public async Task<bool> ApproveAsync(
        Guid tenantId,
        Guid validationId,
        string? certificateHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var validation = await repository.GetByIdAsync(tenantId, validationId, cancellationToken).ConfigureAwait(false);
        if (validation is null)
        {
            return false;
        }

        var transitioned = validation.Approve(now, certificateHash);
        await repository.UpdateAsync(validation, cancellationToken).ConfigureAwait(false);

        // Vincula la identidad vigente al sujeto (idempotente): p.ej. representante.LinkIdentity(validationId).
        // Se hace también en la reaplicación idempotente para auto-curar un anclaje que no llegó a persistir.
        await subjectLinker
            .LinkAsync(tenantId, validation.SubjectType, validation.SubjectRef, validation.Id, actorBy: null, cancellationToken)
            .ConfigureAwait(false);

        return transitioned;
    }

    public async Task<bool> ReconcileAsync(
        Guid tenantId,
        Guid validationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var validation = await repository.GetByIdAsync(tenantId, validationId, cancellationToken).ConfigureAwait(false);
        if (validation is null)
        {
            return false;
        }

        // Estados terminales no se reconcilian.
        if (validation.Status is AdminIdentityEstados.Aprobado or AdminIdentityEstados.Rechazado)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(validation.KyverumVerificationId))
        {
            return false;
        }

        var status = await provider.GetStatusAsync(validation.KyverumVerificationId, cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            return false;
        }

        if (status.Approved)
        {
            return await ApproveAsync(tenantId, validationId, status.CertificateHash, now, cancellationToken).ConfigureAwait(false);
        }

        if (status.Rejected)
        {
            validation.Reject(now, status.ProviderStatus);
            await repository.UpdateAsync(validation, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (status.RejectedAttempt)
        {
            // HU #11504 — un intento falló SIN señal de cierre: el agregado decide si cuenta (dedup por
            // AttemptKey) y si ese conteo alcanza el máximo (terminaliza reutilizando Reject). La decisión
            // vive en el dominio, no aquí.
            var registered = validation.RegisterFailedAttempt(now, status.AttemptKey, status.ProviderStatus);
            if (registered)
            {
                await repository.UpdateAsync(validation, cancellationToken).ConfigureAwait(false);
            }

            return registered;
        }

        // Sigue en proceso: solo se registra la traza del proveedor.
        validation.TrackProvider(status.ProviderStatus, status.RawPayloadSanitized, now);
        await repository.UpdateAsync(validation, cancellationToken).ConfigureAwait(false);
        return false;
    }

    /// <summary>Inicia una validación nueva con el proveedor y la persiste en <c>enviado</c>.</summary>
    private async Task<AdminIdentityValidationResult> StartNewAsync(
        AdminIdentitySubjectDescriptor subject,
        CancellationToken cancellationToken)
    {
        var validationId = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();

        var start = await provider.StartAsync(
            new AdminIdentityStartRequest(
                subject.TenantId,
                validationId,
                subject.SubjectType,
                subject.SubjectRef,
                subject.Name,
                subject.DocumentType,
                subject.DocumentNumber,
                subject.Email),
            cancellationToken).ConfigureAwait(false);

        var validation = AdminIdentityValidation.CreateSent(
            subject.TenantId,
            subject.SubjectType,
            subject.SubjectRef,
            subject.DocumentType,
            subject.DocumentNumber,
            subject.Name,
            subject.Email,
            provider.Name,
            start.CaptureUrl,
            start.VerificationId,
            start.WebhookSecretEncrypted,
            start.ProviderStatus,
            start.RawPayloadSanitized,
            now,
            validationId);

        await repository.AddAsync(validation, cancellationToken).ConfigureAwait(false);
        return new AdminIdentityValidationResult(validation, Reused: false);
    }
}

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
    TimeProvider timeProvider) : IAdminIdentityValidationService
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

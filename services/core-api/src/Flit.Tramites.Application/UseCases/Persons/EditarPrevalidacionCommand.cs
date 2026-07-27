using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Persons;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Entrada para editar una prevalidación standalone — HU #10943 (CF-03, D7). Solo <c>Name</c>/<c>Email</c>
/// (titular) y <c>LegalRepName</c>/<c>LegalRepEmail</c> (representante legal, persona jurídica) son
/// editables. Los campos de documento son OPCIONALES aquí únicamente para detectar un intento de
/// cambiarlos (→ <c>documento_no_editable</c>, 422); nunca se persisten.
/// </summary>
public sealed record EditarPrevalidacionRequest(
    string? Name = null,
    string? Email = null,
    string? LegalRepName = null,
    string? LegalRepEmail = null,
    string? DocumentType = null,
    string? DocumentNumber = null,
    string? LegalRepDocumentType = null,
    string? LegalRepDocumentNumber = null);

/// <summary>
/// Resultado de editar la prevalidación. <see cref="Resent"/> indica si el cambio de correo disparó el
/// reenvío automático (D8); <see cref="CaptureUrl"/> solo viene poblado cuando <see cref="Resent"/> es
/// <c>true</c> y el envío no quedó encolado (proveedor transitorio).
/// </summary>
public sealed record EditarPrevalidacionResult(
    BiometricValidationDto Validation,
    string? CaptureUrl,
    bool Resent);

/// <summary>Resultado de reenviar manualmente una prevalidación standalone — HU #10943 (CF-03, D8).</summary>
public sealed record ReenviarPrevalidacionResult(
    BiometricValidationDto Validation,
    string CaptureUrl,
    bool Queued = false);

// ── Servicio compartido de reenvío (mismo registro, mismo token nuevo — D8) ─────

/// <summary>
/// Núcleo del reenvío (D8) compartido por <see cref="EditarPrevalidacionHandler"/> (auto-reenvío al
/// cambiar el correo) y <see cref="ReenviarPrevalidacionHandler"/> (acción manual): opera SIEMPRE sobre
/// el MISMO registro de validación — nuevo token/enlace, <c>ExpiresAt = now + TokenTtlHoras</c>,
/// <c>Attempts</c>/<c>ReconcilePollCount</c> a 0. NO toca <c>ValidUntil</c> (se estampa solo al aprobar).
/// Aplica el guard D10 (tope 3 reenvíos + cooldown 5 min) ANTES de tocar el proveedor o la fila.
/// </summary>
internal sealed class PrevalidacionResendService(
    IKyverumVerifyClient kyverum,
    BiometricsProviderOptions providerOptions,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events)
{
    public async Task<(string? Error, bool Queued, int? CooldownMinutos, string CaptureUrl)> ResendAsync(
        Guid tenantId,
        ProcedureInstanceBiometricValidation validation,
        IdentitySubjectStandalone subject,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // D10 — tope de reenvíos: bloquea ANTES de consumir una verificación del proveedor.
        if (validation.ResendCount >= BiometricRules.MaxReenvios)
            return ("tope_reenvios", false, null, string.Empty);

        // D10 — cooldown entre reenvíos.
        if (validation.LastResentAt is { } lastResentAt)
        {
            var cooldown = TimeSpan.FromMinutes(BiometricRules.ReenvioCooldownMinutos);
            var elapsed = now - lastResentAt;
            if (elapsed < cooldown)
            {
                var minutosRestantes = (int)Math.Ceiling((cooldown - elapsed).TotalMinutes);
                return ("reenvio_en_cooldown", false, Math.Max(minutosRestantes, 1), string.Empty);
            }
        }

        return providerOptions.IsKyverum
            ? await ResendKyverumAsync(tenantId, validation, subject, now, ct)
            : ResendMock(tenantId, validation, subject, now);
    }

    private async Task<(string? Error, bool Queued, int? CooldownMinutos, string CaptureUrl)> ResendKyverumAsync(
        Guid tenantId,
        ProcedureInstanceBiometricValidation validation,
        IdentitySubjectStandalone subject,
        DateTimeOffset now,
        CancellationToken ct)
    {
        KyverumVerifyStartResult provider;
        try
        {
            provider = await kyverum.StartVerificationAsync(
                new KyverumVerifyStartRequest(
                    ProcedureInstanceId: null,
                    CorrelationId: validation.Id, // MISMO registro (D8): la webhookUrl no cambia.
                    Parte: null,
                    Nombre: subject.Nombre,
                    TipoDoc: subject.TipoDocumento,
                    Documento: subject.NumeroDocumento,
                    Email: subject.Email),
                ct);
        }
        catch (KyverumVerifyException ex)
        {
            if (!ex.Transient)
                return ("proveedor_error", false, null, string.Empty);

            // Falla transitoria (AC11 — HU #10943): encola en pendiente_envio; el worker de envío
            // reintenta. El reenvío YA consumió cupo del tope (D10), aunque el envío no se completó.
            validation.Status = BiometricEstados.PendienteEnvio;
            validation.Attempts = 0;
            validation.ReconcilePollCount = 0;
            validation.ResendCount += 1;
            validation.LastResentAt = now;
            validation.UpdatedAt = now;

            await events.PublishAsync(new IdentityValidationRequested
            {
                TenantId = tenantId,
                ProcedureInstanceId = null,
                ValidationId = validation.Id,
                Provider = BiometricProviders.Kyverum,
                Parte = null,
            }, ct);

            return (null, true, null, string.Empty);
        }

        // Idempotencia con Kyverum (defensa en profundidad, HU #10943): al sobrescribir el secreto del
        // webhook con el de la verificación NUEVA, un webhook tardío de la verificación ANTERIOR (firmado
        // con el secreto viejo) deja de verificar su firma → firma_invalida (401), sin tocar estado ni
        // intentos (ver KyverumWebhookHandler). No requiere cambiar el contrato/mapper de Kyverum.
        validation.KyverumVerificationId = provider.VerificationId;
        validation.CaptureUrl = provider.CaptureUrl;
        validation.WebhookSecretEncrypted = string.IsNullOrEmpty(provider.WebhookSecret)
            ? null
            : secretProtector.Protect(provider.WebhookSecret);
        validation.ProviderStatus = provider.ProviderStatus;
        validation.ProviderPayload = provider.RawPayloadSanitized;
        validation.Status = BiometricEstados.EnProceso;
        validation.Attempts = 0;
        validation.ReconcilePollCount = 0;
        // D8: TTL fijo de 24h en el reenvío (no el expiresAt reportado por el proveedor).
        validation.ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras);
        validation.ResendCount += 1;
        validation.LastResentAt = now;
        validation.UpdatedAt = now;

        await events.PublishAsync(new IdentityValidationRequested
        {
            TenantId = tenantId,
            ProcedureInstanceId = null,
            ValidationId = validation.Id,
            Provider = BiometricProviders.Kyverum,
            Parte = null,
            ProviderVerificationId = provider.VerificationId,
        }, ct);

        return (null, false, null, provider.CaptureUrl);
    }

    private (string? Error, bool Queued, int? CooldownMinutos, string CaptureUrl) ResendMock(
        Guid tenantId,
        ProcedureInstanceBiometricValidation validation,
        IdentitySubjectStandalone subject,
        DateTimeOffset now)
    {
        var token = BiometricToken.Generate();
        validation.TokenHash = BiometricToken.Hash(token);
        validation.Status = BiometricEstados.Enviado;
        validation.Attempts = 0;
        validation.ReconcilePollCount = 0;
        validation.ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras);
        validation.ResendCount += 1;
        validation.LastResentAt = now;
        validation.UpdatedAt = now;

        // El evento se publica de forma sincrónica (no hay proveedor externo que llamar); mantiene el
        // mismo contrato de eventos que IniciarPrevalidacionHandler.
        events.PublishAsync(new IdentityValidationRequested
        {
            TenantId = tenantId,
            ProcedureInstanceId = null,
            ValidationId = validation.Id,
            Provider = BiometricProviders.Mock,
            Parte = null,
        }).GetAwaiter().GetResult();

        var captureUrl = $"/api/v1/public/biometric/{token}";
        return (null, false, null, captureUrl);
    }
}

// ── Handler 1: Editar (PATCH) ────────────────────────────────────────────────

/// <summary>
/// Edita los datos de contacto de una prevalidación standalone — HU #10943 (CF-03, D7/D8/D9/D10/D12).
/// Editable ⟺ <c>ProcedureInstanceId IS NULL AND PersonId IS NOT NULL</c> (D12); documento (titular o RL)
/// NUNCA editable (D7); estado <c>aprobado</c> (vigente o vencida) bloquea la edición (D9). Un cambio de
/// correo (titular o RL, comparación case-insensitive/trim) dispara el reenvío automático en la MISMA
/// transacción (D8); editar solo el nombre NO reenvía.
/// </summary>
public sealed class EditarPrevalidacionHandler(
    IProcedureInstanceRepository repo,
    IKyverumVerifyClient kyverum,
    BiometricsProviderOptions providerOptions,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events,
    IIdentityValidationAuditLog audit)
{
    private readonly PrevalidacionResendService _resend = new(kyverum, providerOptions, secretProtector, events);

    public async Task<(EditarPrevalidacionResult? Result, string? Error, int? CooldownMinutos)> HandleAsync(
        Guid tenantId,
        Guid validationId,
        EditarPrevalidacionRequest input,
        CancellationToken ct = default)
    {
        var validation = await repo.GetBiometricByIdWithPersonAsync(validationId, tenantId, ct);
        if (validation is null)
            return (null, "not_found", null);

        // D12 — solo standalone. Defensa en profundidad con PersonId (invariante de BD ya lo exige).
        if (validation.ProcedureInstanceId is not null || validation.PersonId is null || validation.Person is null)
            return (null, "no_editable", null);

        // D9 — aprobado (vigente O vencida) bloquea. La vigencia NO importa aquí: el bloqueo es por
        // ESTADO, no por fecha (revalidar exige crear una prevalidación nueva).
        if (validation.Status == BiometricEstados.Aprobado)
            return (null, "identidad_aprobada", null);

        var person = validation.Person;

        // D7 — documento (titular o RL) NUNCA editable. Se valida ANTES de tocar nada (ni persona ni
        // validación cambian, AC6).
        if (HasDocumentChangeAttempt(input, person))
            return (null, "documento_no_editable", null);

        var now = DateTimeOffset.UtcNow;
        var subjectBefore = IniciarPrevalidacionHandler.ResolveSubject(person);
        var emailBefore = subjectBefore.Email.Trim();

        // Calcula los valores PROSPECTIVOS sobre una copia liviana, SIN mutar la entidad `person`
        // rastreada: si el guard de reenvío (D10) bloquea más abajo, ni la persona ni la validación
        // deben quedar con cambios a medias (ni siquiera en memoria).
        var prospective = new Person
        {
            Id = person.Id,
            TenantId = person.TenantId,
            DocumentType = person.DocumentType,
            DocumentNumber = person.DocumentNumber,
            FullName = !string.IsNullOrWhiteSpace(input.Name) ? input.Name.Trim() : person.FullName,
            Email = !string.IsNullOrWhiteSpace(input.Email) ? input.Email.Trim() : person.Email,
            PersonType = person.PersonType,
            LegalRepDocumentType = person.LegalRepDocumentType,
            LegalRepDocumentNumber = person.LegalRepDocumentNumber,
            LegalRepName = person.PersonType == PersonTypes.Juridical && !string.IsNullOrWhiteSpace(input.LegalRepName)
                ? input.LegalRepName.Trim()
                : person.LegalRepName,
            LegalRepEmail = person.PersonType == PersonTypes.Juridical && !string.IsNullOrWhiteSpace(input.LegalRepEmail)
                ? input.LegalRepEmail.Trim()
                : person.LegalRepEmail,
        };

        var subjectAfter = IniciarPrevalidacionHandler.ResolveSubject(prospective);
        var emailAfter = subjectAfter.Email.Trim();
        var emailChanged = !string.Equals(emailBefore, emailAfter, StringComparison.OrdinalIgnoreCase);

        string? captureUrl = null;

        if (emailChanged)
        {
            // D10: el cambio de correo consume cupo igual que un reenvío manual — el guard corre ANTES
            // de aplicar nada (ni a `person` ni a `validation`), así que un bloqueo no deja cambios a medias.
            var (error, _, cooldownMinutos, resendCaptureUrl) =
                await _resend.ResendAsync(tenantId, validation, subjectAfter, now, ct);
            if (error is not null)
                return (null, error, cooldownMinutos);

            captureUrl = string.IsNullOrEmpty(resendCaptureUrl) ? null : resendCaptureUrl;

            // AC13 (Habeas Data) — auditoría del cambio de correo con valores ENMASCARADOS. Nunca en claro.
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.ContactEdited,
                IdentityValidationAuditOutcomes.Ok,
                TenantId: tenantId,
                ValidationId: validation.Id,
                Message: $"Cambio de correo en prevalidación: {MaskEmail(emailBefore)} -> {MaskEmail(emailAfter)}.",
                Detail: $"correo_anterior={MaskEmail(emailBefore)}; correo_nuevo={MaskEmail(emailAfter)}"), ct);
        }

        // Guards superados (o sin cambio de correo): ahora sí se aplican los cambios reales.
        person.FullName = prospective.FullName;
        person.Email = prospective.Email;
        person.LegalRepName = prospective.LegalRepName;
        person.LegalRepEmail = prospective.LegalRepEmail;
        person.UpdatedAt = now;

        validation.Name = subjectAfter.Nombre;
        validation.Email = subjectAfter.Email;
        validation.UpdatedAt = now;

        await repo.SaveChangesAsync(ct);

        var dto = IniciarBiometriaHandler.ToDto(validation, now);
        return (new EditarPrevalidacionResult(dto, captureUrl, emailChanged), null, null);
    }

    /// <summary>D7 — ¿el request intenta cambiar el tipo/número de documento (titular o RL)?</summary>
    private static bool HasDocumentChangeAttempt(EditarPrevalidacionRequest input, Person person) =>
        DifferentAndProvided(input.DocumentType, person.DocumentType)
        || DifferentAndProvided(input.DocumentNumber, person.DocumentNumber)
        || DifferentAndProvided(input.LegalRepDocumentType, person.LegalRepDocumentType)
        || DifferentAndProvided(input.LegalRepDocumentNumber, person.LegalRepDocumentNumber);

    private static bool DifferentAndProvided(string? incoming, string? current)
    {
        if (string.IsNullOrWhiteSpace(incoming))
            return false;
        return !string.Equals(incoming.Trim(), (current ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Enmascara un correo para auditoría/Habeas Data (@pii:high): conserva 1-2 caracteres del local-part
    /// y el dominio completo (p.ej. <c>an***@old.com</c>). NUNCA se loguea el correo en claro (AC13).
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "***";
        var at = email.IndexOf('@');
        if (at <= 0)
            return "***";
        var local = email[..at];
        var domain = email[(at + 1)..];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}***@{domain}";
    }
}

// ── Handler 2: Reenviar (POST /resend) ───────────────────────────────────────

/// <summary>
/// Reenvía manualmente la validación de identidad de una prevalidación standalone, sin editar ningún
/// dato — HU #10943 (CF-03, D8/D9/D10/D12). Mismos guards de editabilidad/estado que
/// <see cref="EditarPrevalidacionHandler"/>; el correo destino es el del sujeto VIGENTE (sin cambios).
/// </summary>
public sealed class ReenviarPrevalidacionHandler(
    IProcedureInstanceRepository repo,
    IKyverumVerifyClient kyverum,
    BiometricsProviderOptions providerOptions,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events)
{
    private readonly PrevalidacionResendService _resend = new(kyverum, providerOptions, secretProtector, events);

    public async Task<(ReenviarPrevalidacionResult? Result, string? Error, int? CooldownMinutos)> HandleAsync(
        Guid tenantId,
        Guid validationId,
        CancellationToken ct = default)
    {
        var validation = await repo.GetBiometricByIdWithPersonAsync(validationId, tenantId, ct);
        if (validation is null)
            return (null, "not_found", null);

        if (validation.ProcedureInstanceId is not null || validation.PersonId is null || validation.Person is null)
            return (null, "no_editable", null);

        if (validation.Status == BiometricEstados.Aprobado)
            return (null, "identidad_aprobada", null);

        var now = DateTimeOffset.UtcNow;
        var subject = IniciarPrevalidacionHandler.ResolveSubject(validation.Person);

        var (error, queued, cooldownMinutos, captureUrl) =
            await _resend.ResendAsync(tenantId, validation, subject, now, ct);
        if (error is not null)
            return (null, error, cooldownMinutos);

        validation.Name = subject.Nombre;
        validation.Email = subject.Email;

        await repo.SaveChangesAsync(ct);

        var dto = IniciarBiometriaHandler.ToDto(validation, now);
        return (new ReenviarPrevalidacionResult(dto, captureUrl, queued), null, null);
    }
}

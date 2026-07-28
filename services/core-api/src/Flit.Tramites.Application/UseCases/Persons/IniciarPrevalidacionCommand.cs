using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Persons;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Entrada para crear una prevalidación de identidad standalone (sin trámite previo) — HU #10866, CF-01.
/// Para persona jurídica, los campos <c>LegalRep*</c> identifican al representante legal que realiza
/// la validación biométrica (el sujeto real de la validación, no el NIT).
/// </summary>
public sealed record IniciarPrevalidacionRequest(
    string DocumentType,
    string DocumentNumber,
    string Name,
    string Email,
    string PersonType = PersonTypes.Natural,
    string? LegalRepDocumentType = null,
    string? LegalRepDocumentNumber = null,
    string? LegalRepName = null,
    string? LegalRepEmail = null);

/// <summary>
/// Resultado de crear la prevalidación standalone. <see cref="Queued"/> = true cuando el proveedor
/// Kyverum falló de forma transitoria y la validación quedó encolada para reintento (202 Accepted).
/// </summary>
public sealed record IniciarPrevalidacionResult(
    BiometricValidationDto Validation,
    string CaptureUrl,
    bool Queued = false);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Crea una prevalidación de identidad standalone (sin trámite previo) — HU #10866, CF-01, ADR-0030.
///
/// <para>Flujo:
/// <list type="number">
///   <item>Valida los campos obligatorios (y datos del RL si es persona jurídica).</item>
///   <item>Hace upsert de la entidad <see cref="Person"/> por <c>(tenant, docType, docNum)</c>.</item>
///   <item>Guard 409: si ya hay una prevalidación standalone activa para esa persona, retorna
///        <c>prevalidacion_activa</c>.</item>
///   <item>Resuelve el <c>IdentitySubject</c>: persona natural → datos directos; jurídica → datos del RL.</item>
///   <item>Kyverum: inicia la verificación con <c>ProcedureInstanceId = null</c>. Mock: crea en estado
///        <c>enviado</c> con token de magic-link.</item>
///   <item>Persiste la validación con <c>PersonId = person.Id</c>, <c>ProcedureInstanceId = null</c>,
///        <c>PartyRole = null</c>.</item>
///   <item>Emite el evento <see cref="IdentityValidationRequested"/> (outbox).</item>
/// </list>
/// </para>
///
/// <para>No modifica <c>IniciarKyverumVerifyHandler</c> — handler separado para standalone (diseño §9).</para>
/// </summary>
public sealed class IniciarPrevalidacionHandler(
    IPersonRepository personRepo,
    IProcedureInstanceRepository procedureRepo,
    IKyverumVerifyClient kyverum,
    BiometricsProviderOptions providerOptions,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events)
{
    public async Task<(IniciarPrevalidacionResult? Result, string? Error)> HandleAsync(
        Guid tenantId,
        IniciarPrevalidacionRequest input,
        CancellationToken ct = default)
    {
        // ── 1. Validación de entrada ─────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(input.DocumentType)
            || string.IsNullOrWhiteSpace(input.DocumentNumber)
            || string.IsNullOrWhiteSpace(input.Name)
            || string.IsNullOrWhiteSpace(input.Email))
            return (null, "datos_incompletos");

        var personType = string.IsNullOrWhiteSpace(input.PersonType)
            ? PersonTypes.Natural
            : input.PersonType.Trim().ToLowerInvariant();

        if (personType == PersonTypes.Juridical)
        {
            if (string.IsNullOrWhiteSpace(input.LegalRepDocumentType)
                || string.IsNullOrWhiteSpace(input.LegalRepDocumentNumber)
                || string.IsNullOrWhiteSpace(input.LegalRepName))
                return (null, "datos_representante_requerido");
        }

        // ── 2. Upsert de la entidad persona ─────────────────────────────────────
        var person = await personRepo.FindOrCreateAsync(
            tenantId,
            input.DocumentType.Trim(),
            input.DocumentNumber.Trim(),
            input.Name.Trim(),
            input.Email.Trim(),
            personType,
            input.LegalRepDocumentType?.Trim(),
            input.LegalRepDocumentNumber?.Trim(),
            input.LegalRepName?.Trim(),
            input.LegalRepEmail?.Trim(),
            ct);

        // ── 3. Guard 409: prevalidación activa existente ─────────────────────────
        // Comprobamos por PersonId (ya sea persona nueva recién añadida al contexto o existente).
        // Si el person.Id ya tiene una prevalidación activa, el gestor debe reusar la existente.
        // Solo aplica si el person ya existe en BD (Id conocido sin SaveChanges aún).
        // Para persona nueva, no puede haber prevalidación activa → el check devuelve null.
        var existingActive = await personRepo.FindActiveStandaloneValidationAsync(person.Id, ct);
        if (existingActive is not null)
            return (null, "prevalidacion_activa");

        // ── 4. Sujeto de identidad (AC2: jurídica → RL) ──────────────────────────
        var subject = ResolveSubject(person);
        if (string.IsNullOrWhiteSpace(subject.Nombre)
            || string.IsNullOrWhiteSpace(subject.TipoDocumento)
            || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            return (null, "datos_incompletos");

        var validationId = Guid.NewGuid();

        // ── 5a. Proveedor Kyverum ─────────────────────────────────────────────────
        if (providerOptions.IsKyverum)
            return await IniciarConKyverumAsync(tenantId, person, subject, validationId, ct);

        // ── 5b. Proveedor mock ────────────────────────────────────────────────────
        return await IniciarConMockAsync(tenantId, person, subject, validationId, ct);
    }

    // ── Kyverum path ─────────────────────────────────────────────────────────────

    private async Task<(IniciarPrevalidacionResult? Result, string? Error)> IniciarConKyverumAsync(
        Guid tenantId, Person person, IdentitySubjectStandalone subject,
        Guid validationId, CancellationToken ct)
    {
        KyverumVerifyStartResult provider;
        try
        {
            provider = await kyverum.StartVerificationAsync(
                new KyverumVerifyStartRequest(
                    ProcedureInstanceId: null,
                    CorrelationId: validationId,
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
                return (null, "proveedor_error");

            var queuedAt = DateTimeOffset.UtcNow;
            var queued = BuildValidation(tenantId, person.Id, validationId, subject,
                status: BiometricEstados.PendienteEnvio,
                provider: BiometricProviders.Kyverum,
                now: queuedAt,
                maxAttempts: BiometricRules.KyverumMaxIntentos);
            queued.Attempts = 1;
            procedureRepo.Add(queued);

            await events.PublishAsync(new IdentityValidationRequested
            {
                TenantId = tenantId,
                ProcedureInstanceId = null,
                ValidationId = queued.Id,
                Provider = BiometricProviders.Kyverum,
                Parte = null,
            }, ct);

            await procedureRepo.SaveChangesAsync(ct);

            var queuedDto = IniciarBiometriaHandler.ToDto(queued, queuedAt);
            return (new IniciarPrevalidacionResult(queuedDto, string.Empty, Queued: true), null);
        }

        var now = DateTimeOffset.UtcNow;
        var validation = BuildValidation(tenantId, person.Id, validationId, subject,
            status: BiometricEstados.EnProceso,
            provider: BiometricProviders.Kyverum,
            now: now,
            maxAttempts: BiometricRules.KyverumMaxIntentos,
            expiresAt: provider.ExpiresAt);

        validation.KyverumVerificationId = provider.VerificationId;
        validation.CaptureUrl = provider.CaptureUrl;
        validation.WebhookSecretEncrypted = string.IsNullOrEmpty(provider.WebhookSecret)
            ? null
            : secretProtector.Protect(provider.WebhookSecret);
        validation.ProviderStatus = provider.ProviderStatus;
        validation.ProviderPayload = provider.RawPayloadSanitized;

        procedureRepo.Add(validation);

        await events.PublishAsync(new IdentityValidationRequested
        {
            TenantId = tenantId,
            ProcedureInstanceId = null,
            ValidationId = validation.Id,
            Provider = BiometricProviders.Kyverum,
            Parte = null,
            ProviderVerificationId = provider.VerificationId,
        }, ct);

        await procedureRepo.SaveChangesAsync(ct);

        var dto = IniciarBiometriaHandler.ToDto(validation, now);
        return (new IniciarPrevalidacionResult(dto, provider.CaptureUrl), null);
    }

    // ── Mock path ────────────────────────────────────────────────────────────────

    private async Task<(IniciarPrevalidacionResult? Result, string? Error)> IniciarConMockAsync(
        Guid tenantId, Person person, IdentitySubjectStandalone subject,
        Guid validationId, CancellationToken ct)
    {
        var token = BiometricToken.Generate();
        var now = DateTimeOffset.UtcNow;

        var validation = BuildValidation(tenantId, person.Id, validationId, subject,
            status: BiometricEstados.Enviado,
            provider: BiometricProviders.Mock,
            now: now,
            maxAttempts: BiometricRules.MaxIntentos,
            tokenHash: BiometricToken.Hash(token));

        procedureRepo.Add(validation);

        await events.PublishAsync(new IdentityValidationRequested
        {
            TenantId = tenantId,
            ProcedureInstanceId = null,
            ValidationId = validation.Id,
            Provider = BiometricProviders.Mock,
            Parte = null,
        }, ct);

        await procedureRepo.SaveChangesAsync(ct);

        var dto = IniciarBiometriaHandler.ToDto(validation, now);
        var captureUrl = $"/api/v1/public/biometric/{token}";
        return (new IniciarPrevalidacionResult(dto, captureUrl), null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Construye la entidad <see cref="ProcedureInstanceBiometricValidation"/> para la prevalidación
    /// standalone. <c>ProcedureInstanceId = null</c> y <c>PartyRole = null</c> (sin trámite ni parte).
    /// </summary>
    private static ProcedureInstanceBiometricValidation BuildValidation(
        Guid tenantId,
        Guid personId,
        Guid validationId,
        IdentitySubjectStandalone subject,
        string status,
        string provider,
        DateTimeOffset now,
        int maxAttempts,
        DateTimeOffset? expiresAt = null,
        string? tokenHash = null)
    {
        return new ProcedureInstanceBiometricValidation
        {
            Id = validationId,
            TenantId = tenantId,
            PersonId = personId,
            ProcedureInstanceId = null,
            PartyRole = null,
            Name = subject.Nombre,
            DocumentType = subject.TipoDocumento,
            DocumentNumber = subject.NumeroDocumento,
            Email = subject.Email,
            Status = status,
            TokenHash = tokenHash ?? BiometricToken.Hash(BiometricToken.Generate()),
            ExpiresAt = expiresAt ?? now.AddHours(BiometricRules.TokenTtlHoras),
            Attempts = 0,
            MaxAttempts = maxAttempts,
            Provider = provider,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Resuelve el sujeto de identidad de la entidad <see cref="Person"/> para el contexto standalone
    /// (AC2: jurídica → datos del RL). Equivalente funcional a <c>IdentitySubjectResolver.For(actor)</c>
    /// pero sin depender de <c>ProcedureInstanceActor</c>. <c>internal</c> — reutilizado por
    /// <c>EditarPrevalidacionHandler</c>/<c>ReenviarPrevalidacionHandler</c> (HU #10943, CF-03) para
    /// resolver el correo destino del reenvío sin duplicar la regla natural/jurídica.
    /// </summary>
    internal static IdentitySubjectStandalone ResolveSubject(Person person)
    {
        if (person.PersonType == PersonTypes.Juridical
            && !string.IsNullOrWhiteSpace(person.LegalRepDocumentType)
            && !string.IsNullOrWhiteSpace(person.LegalRepDocumentNumber))
        {
            return new IdentitySubjectStandalone(
                Nombre: !string.IsNullOrWhiteSpace(person.LegalRepName)
                    ? person.LegalRepName
                    : person.FullName,
                TipoDocumento: person.LegalRepDocumentType,
                NumeroDocumento: person.LegalRepDocumentNumber,
                Email: !string.IsNullOrWhiteSpace(person.LegalRepEmail)
                    ? person.LegalRepEmail
                    : person.Email);
        }

        return new IdentitySubjectStandalone(
            Nombre: person.FullName,
            TipoDocumento: person.DocumentType,
            NumeroDocumento: person.DocumentNumber,
            Email: person.Email);
    }
}

/// <summary>
/// Sujeto de identidad resuelto desde la entidad <see cref="Person"/> para el contexto standalone.
/// Equivalente a <see cref="IdentitySubject"/> pero sin la dependencia del actor del trámite.
/// </summary>
internal sealed record IdentitySubjectStandalone(
    string Nombre,
    string TipoDocumento,
    string NumeroDocumento,
    string Email);

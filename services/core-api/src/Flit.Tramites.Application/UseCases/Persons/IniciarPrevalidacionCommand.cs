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
    public async Task<(IniciarPrevalidacionResult? Result, string? Error, IdentitySendDecision? Conflict)> HandleAsync(
        Guid tenantId,
        IniciarPrevalidacionRequest input,
        CancellationToken ct = default)
    {
        // ── 1. Validación de entrada ─────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(input.DocumentType)
            || string.IsNullOrWhiteSpace(input.DocumentNumber)
            || string.IsNullOrWhiteSpace(input.Name)
            || string.IsNullOrWhiteSpace(input.Email))
            return (null, "datos_incompletos", null);

        var personType = string.IsNullOrWhiteSpace(input.PersonType)
            ? PersonTypes.Natural
            : input.PersonType.Trim().ToLowerInvariant();

        // CF-01/D1 (ADR-0036, Feature #11004) — la prevalidación standalone SOLO admite persona natural;
        // la validación de actores jurídicos queda exclusiva del flujo de trámite (IdentitySubjectResolver
        // sobre ProcedureInstanceActor, sin cambios). Guard evaluado ANTES del upsert de Person para no
        // crear/actualizar un registro que de todas formas se rechaza. No es solo un cambio de UI: cierra
        // la puerta trasera server-side aunque el body llegue directo a la API con personType=juridical.
        if (personType == PersonTypes.Juridical)
            return (null, "prevalidacion_solo_natural", null);

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

        // ── 3. Precedencia de envío (HU #11264 / ADR-0039) ───────────────────────
        // Capa PREVIA al guard histórico: consulta baúl N/A en standalone natural, identidad vigente
        // por documento y validación en vuelo (con/sin enlace vencido). Conserva el código
        // "prevalidacion_activa" para NoEnviar (compat contrato/tests) y "enlace_vencido_reenvio"
        // cuando corresponde encauzar al reenvío sin crear fila nueva.
        var now = DateTimeOffset.UtcNow;
        var tipoDoc = input.DocumentType.Trim();
        var numeroDoc = input.DocumentNumber.Trim();
        var candidates = new List<ProcedureInstanceBiometricValidation>();
        var existingActive = await personRepo.FindActiveStandaloneValidationAsync(person.Id, ct);
        if (existingActive is not null)
            candidates.Add(existingActive);
        var vigente = await procedureRepo.FindVigenteApprovedByDocumentAsync(
            tenantId, tipoDoc, numeroDoc, now, ct);
        if (vigente is not null
            && candidates.TrueForAll(c => c.Id != vigente.Id))
            candidates.Add(vigente);

        var decision = IdentitySendDecisionEvaluator.Evaluate(new IdentitySendDecisionContext(
            tenantId,
            tipoDoc,
            numeroDoc,
            Actor: null,
            HasBaulFirmaActivaVigente: false,
            ValidationsForPerson: candidates,
            Now: now));

        if (decision.Kind == IdentitySendDecisionKind.EncauzarReenvio)
            return (null, "enlace_vencido_reenvio", decision);

        if (decision.Kind == IdentitySendDecisionKind.NoEnviar)
            return (null, "prevalidacion_activa", decision);

        // ── 4. Sujeto de identidad (AC2: jurídica → RL) ──────────────────────────
        var subject = ResolveSubject(person);
        if (string.IsNullOrWhiteSpace(subject.Nombre)
            || string.IsNullOrWhiteSpace(subject.TipoDocumento)
            || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            return (null, "datos_incompletos", null);

        var validationId = Guid.NewGuid();

        // ── 5a. Proveedor Kyverum ─────────────────────────────────────────────────
        if (providerOptions.IsKyverum)
        {
            var (result, error, conflict) = await IniciarConKyverumAsync(tenantId, person.Id, null, subject, validationId, ct);
            return (result, error, conflict);
        }

        // ── 5b. Proveedor mock ────────────────────────────────────────────────────
        {
            var (result, error, conflict) = await IniciarConMockAsync(tenantId, person.Id, null, subject, validationId, ct);
            return (result, error, conflict);
        }
    }

    /// <summary>
    /// HU #13246 (Feature #13245, Épica #13090) — lanza la validación de identidad PROPIA de un mandatario con el MISMO flujo
    /// de la prevalidación y del trámite (Kyverum: enlace al correo, captura y webhook; mock en local). Diferencias
    /// deliberadas respecto de <see cref="HandleAsync"/>:
    /// <list type="bullet">
    ///   <item>La validación queda con <c>PartyRole = mandatario</c> y <c>MandateSignerId</c> de la ficha, en el
    ///        <paramref name="tenantId"/> de la COMPAÑÍA del mandatario (también cuando la crea el OT).</item>
    ///   <item>Sin <see cref="Person"/> ni trámite: el ancla es la ficha. No evalúa la precedencia de envío por documento
    ///        (una identidad vigente del mismo documento de un comprador, vendedor o prevalidación NO cuenta ni
    ///        bloquea): solo le importa lo que ya hay para ESE mandatario.</item>
    ///   <item>Las validaciones en vuelo del mismo mandatario se cierran antes (<c>expirado</c>): la anterior deja de contar.
    ///        Dos lanzamientos simultáneos dejan una sola activa (índice único parcial por mandatario → error
    ///        <c>prevalidacion_activa</c> para el que pierde la carrera).</item>
    /// </list>
    /// No cambia el contrato de <see cref="HandleAsync"/>.
    /// </summary>
    public async Task<(IniciarPrevalidacionResult? Result, string? Error)> HandleMandatarioAsync(
        Guid tenantId,
        Guid mandateSignerId,
        string documentType,
        string documentNumber,
        string name,
        string email,
        CancellationToken ct = default)
    {
        if (mandateSignerId == Guid.Empty
            || string.IsNullOrWhiteSpace(documentType)
            || string.IsNullOrWhiteSpace(documentNumber)
            || string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(email))
            return (null, "datos_incompletos");

        var subject = new IdentitySubjectStandalone(
            name.Trim(), documentType.Trim(), documentNumber.Trim(), email.Trim());

        // La anterior deja de contar: se cierra la que siga en vuelo antes de crear la nueva (el índice único parcial
        // por mandatario no admite dos). El resto del historial se conserva.
        await procedureRepo.SupersedeMandatarioInFlightAsync(mandateSignerId, DateTimeOffset.UtcNow, ct);

        var validationId = Guid.NewGuid();
        var (result, error, _) = providerOptions.IsKyverum
            ? await IniciarConKyverumAsync(tenantId, null, mandateSignerId, subject, validationId, ct)
            : await IniciarConMockAsync(tenantId, null, mandateSignerId, subject, validationId, ct);
        return (result, error);
    }

    // ── Kyverum path ─────────────────────────────────────────────────────────────

    private async Task<(IniciarPrevalidacionResult? Result, string? Error, IdentitySendDecision? Conflict)> IniciarConKyverumAsync(
        Guid tenantId, Guid? personId, Guid? mandateSignerId, IdentitySubjectStandalone subject,
        Guid validationId, CancellationToken ct)
    {
        KyverumVerifyStartResult provider;
        try
        {
            provider = await kyverum.StartVerificationAsync(
                new KyverumVerifyStartRequest(
                    ProcedureInstanceId: null,
                    CorrelationId: validationId,
                    Parte: mandateSignerId is null ? null : BiometricRules.ParteMandatario,
                    Nombre: subject.Nombre,
                    TipoDoc: subject.TipoDocumento,
                    Documento: subject.NumeroDocumento,
                    Email: subject.Email),
                ct);
        }
        catch (KyverumVerifyException ex)
        {
            if (!ex.Transient)
                return (null, "proveedor_error", null);

            var queuedAt = DateTimeOffset.UtcNow;
            var queued = BuildValidation(tenantId, personId, mandateSignerId, validationId, subject,
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
                Parte = mandateSignerId is null ? null : BiometricRules.ParteMandatario,
            }, ct);

            try
            {
                await procedureRepo.SaveChangesAsync(ct);
            }
            catch (Domain.Identity.IdentityInFlightConflictException)
            {
                return (null, "prevalidacion_activa",
                    IdentitySendDecisionForTramite.InFlightRaceConflict(IdentitySendOrigen.Standalone));
            }

            var queuedDto = IniciarBiometriaHandler.ToDto(queued, queuedAt);
            return (new IniciarPrevalidacionResult(queuedDto, string.Empty, Queued: true), null, null);
        }

        var now = DateTimeOffset.UtcNow;
        var validation = BuildValidation(tenantId, personId, mandateSignerId, validationId, subject,
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
            Parte = mandateSignerId is null ? null : BiometricRules.ParteMandatario,
            ProviderVerificationId = provider.VerificationId,
        }, ct);

        try
        {
            await procedureRepo.SaveChangesAsync(ct);
        }
        catch (Domain.Identity.IdentityInFlightConflictException)
        {
            return (null, "prevalidacion_activa",
                IdentitySendDecisionForTramite.InFlightRaceConflict(IdentitySendOrigen.Standalone));
        }

        var dto = IniciarBiometriaHandler.ToDto(validation, now);
        return (new IniciarPrevalidacionResult(dto, provider.CaptureUrl), null, null);
    }

    // ── Mock path ────────────────────────────────────────────────────────────────

    private async Task<(IniciarPrevalidacionResult? Result, string? Error, IdentitySendDecision? Conflict)> IniciarConMockAsync(
        Guid tenantId, Guid? personId, Guid? mandateSignerId, IdentitySubjectStandalone subject,
        Guid validationId, CancellationToken ct)
    {
        var token = BiometricToken.Generate();
        var now = DateTimeOffset.UtcNow;

        var validation = BuildValidation(tenantId, personId, mandateSignerId, validationId, subject,
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
            Parte = mandateSignerId is null ? null : BiometricRules.ParteMandatario,
        }, ct);

        try
        {
            await procedureRepo.SaveChangesAsync(ct);
        }
        catch (Domain.Identity.IdentityInFlightConflictException)
        {
            return (null, "prevalidacion_activa",
                IdentitySendDecisionForTramite.InFlightRaceConflict(IdentitySendOrigen.Standalone));
        }

        var dto = IniciarBiometriaHandler.ToDto(validation, now);
        var captureUrl = $"/api/v1/public/biometric/{token}";
        return (new IniciarPrevalidacionResult(dto, captureUrl), null, null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Construye la entidad <see cref="ProcedureInstanceBiometricValidation"/> para la prevalidación
    /// standalone. <c>ProcedureInstanceId = null</c> y <c>PartyRole = null</c> (sin trámite ni parte).
    /// </summary>
    private static ProcedureInstanceBiometricValidation BuildValidation(
        Guid tenantId,
        Guid? personId,
        Guid? mandateSignerId,
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
            // HU #13246 — con mandateSignerId la validación es del mandatario (exclusiva de su ficha).
            PartyRole = mandateSignerId is null ? null : BiometricRules.ParteMandatario,
            MandateSignerId = mandateSignerId,
            Name = subject.Nombre,
            DocumentType = subject.TipoDocumento,
            DocumentNumber = subject.NumeroDocumento,
            Email = subject.Email,
            RegisteredEmail = subject.Email,
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
            // Misma regla que IdentitySubjectResolver: en jurídica el correo es SOLO el del RL.
            // Sin LegalRepEmail no se cae al correo de la empresa (person.Email).
            return new IdentitySubjectStandalone(
                Nombre: !string.IsNullOrWhiteSpace(person.LegalRepName)
                    ? person.LegalRepName
                    : person.FullName,
                TipoDocumento: person.LegalRepDocumentType,
                NumeroDocumento: person.LegalRepDocumentNumber,
                Email: !string.IsNullOrWhiteSpace(person.LegalRepEmail)
                    ? person.LegalRepEmail!
                    : string.Empty);
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

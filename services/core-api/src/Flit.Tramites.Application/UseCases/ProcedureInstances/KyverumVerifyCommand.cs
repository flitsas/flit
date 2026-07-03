using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Resultado de iniciar una validación Kyverum: la validación + la URL de captura. Si <see cref="Queued"/>
/// es true, el envío falló de forma transitoria y quedó ENCOLADO para reintento (la validación está en
/// <c>pendiente_envio</c> y aún no hay <c>CaptureUrl</c>).
/// </summary>
public sealed record IniciarKyverumVerifyResult(
    BiometricValidationDto Validation, string CaptureUrl, bool Queued = false);

/// <summary>
/// Entrada del webhook de Kyverum: id de NUESTRA validación (de la URL del callback, ya que el cuerpo
/// no lo repite), cuerpo CRUDO (para HMAC) y la firma del header <c>x-kv-signature</c>.
/// </summary>
public sealed record KyverumWebhookInput(Guid ValidationId, byte[] RawBody, string? Signature);

// ── Handler: iniciar validación Kyverum (autenticado) — AC1 ───────────────────

/// <summary>
/// Inicia una validación de identidad con Kyverum (HU #10233, AC1). Mismas precondiciones que el flujo
/// mock (instancia en draft, idempotencia por parte), pero delega la captura al proveedor: llama a
/// <see cref="IKyverumVerifyClient"/>, persiste provider/verification_id/capture_url + el secreto del
/// webhook CIFRADO (Data Protection) + el payload sanitizado, deja la validación en <c>en_proceso</c>
/// y emite el evento <see cref="IdentityValidationRequested"/> (outbox). El token_hash se genera al azar
/// (no se usa magic-link) solo para satisfacer la columna NOT NULL/única. Errores del proveedor →
/// <c>proveedor_no_disponible</c> (transitorio, 503) / <c>proveedor_error</c> (definitivo, 502): AC7.
/// </summary>
public sealed class IniciarKyverumVerifyHandler(
    IProcedureInstanceRepository repo,
    IKyverumVerifyClient kyverum,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events,
    IIdentityValidationAuditLog audit)
{
    public async Task<(IniciarKyverumVerifyResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        IniciarBiometriaInput input,
        CancellationToken ct = default)
    {
        var parte = NormalizeParte(input.Parte);
        if (parte is "invalid")
            return (null, "parte_invalida");

        // Se cargan también los actores: el wizard dispara la validación enviando SOLO la parte y los
        // datos del sujeto se toman del actor del trámite (fuente única de verdad). Mismo repo que
        // SimularBiometriaHandler.
        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");
        if (instance.Status != TramiteEstado.Borrador)
            return (null, "not_draft");

        var existing = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
            && v.Status is BiometricEstados.Enviado or BiometricEstados.EnProceso
                or BiometricEstados.Aprobado or BiometricEstados.PendienteEnvio);
        if (existing is not null)
            return (null, "biometria_activa");

        // Datos del sujeto: el body los puede sobreescribir (API/Postman directo); si vienen vacíos, se
        // resuelven desde el actor de la parte registrado en el trámite (el camino del wizard).
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));
        var nombre = FirstNonEmpty(input.Nombre, actor?.FullName);
        var tipoDoc = FirstNonEmpty(input.TipoDoc, actor?.DocumentType);
        var documento = FirstNonEmpty(input.Documento, actor?.DocumentNumber);
        var email = FirstNonEmpty(input.Email, actor?.Email);
        if (nombre is null || tipoDoc is null || documento is null || email is null)
            // Sin datos: si ni siquiera hay actor registrado → actor_requerido; si el actor existe pero le
            // falta algún dato (p.ej. email) → datos_incompletos.
            return (null, actor is null ? "actor_requerido" : "datos_incompletos");

        // Id de NUESTRA validación: se genera ANTES de llamar al proveedor para incrustarlo en la
        // webhookUrl (el webhook de Kyverum no repite el id en el cuerpo → correlación por URL).
        var validationId = Guid.NewGuid();

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.Send, IdentityValidationAuditOutcomes.Ok,
            TenantId: tenantId, ProcedureInstanceId: id, ValidationId: validationId, PartyRole: parte,
            Message: "Enviando validación a Kyverum (create)."), ct);

        // Llamada al proveedor. NUNCA propaga la API key ni el secreto en el mensaje de error (AC7).
        KyverumVerifyStartResult provider;
        try
        {
            provider = await kyverum.StartVerificationAsync(
                new KyverumVerifyStartRequest(id, validationId, parte, nombre, tipoDoc, documento, email),
                ct);
        }
        catch (KyverumVerifyException ex)
        {
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.SendError, IdentityValidationAuditOutcomes.Error,
                TenantId: tenantId, ProcedureInstanceId: id, ValidationId: validationId, PartyRole: parte,
                ErrorType: nameof(KyverumVerifyException), HttpStatus: ex.Transient ? 503 : 502,
                Message: ex.Transient ? "Create falló (transitorio): se encola para reintento." : "Create rechazado (definitivo)."), ct);

            // Fallo DEFINITIVO (datos/4xx): no se reintenta.
            if (!ex.Transient)
                return (null, "proveedor_error");

            // Fallo TRANSITORIO (proveedor caído/timeout/5xx): se ENCOLA para reintento. Persiste la
            // validación en pendiente_envio con los datos del sujeto; el worker (provider-agnostic)
            // reintentará el envío sin intervención del gestor y la pasará a en_proceso al lograrlo.
            var queuedAt = DateTimeOffset.UtcNow;
            var queued = new ProcedureInstanceBiometricValidation
            {
                Id = validationId,
                TenantId = tenantId,
                ProcedureInstanceId = id,
                PartyRole = parte,
                Name = nombre,
                DocumentType = tipoDoc,
                DocumentNumber = documento,
                Email = email,
                Status = BiometricEstados.PendienteEnvio,
                TokenHash = BiometricToken.Hash(BiometricToken.Generate()),
                ExpiresAt = queuedAt.AddHours(BiometricRules.TokenTtlHoras),
                Attempts = 1, // el primer intento (síncrono) ya falló
                MaxAttempts = BiometricRules.KyverumMaxIntentos,
                CreatedAt = queuedAt,
                Provider = BiometricProviders.Kyverum,
            };
            instance.BiometricValidations.Add(queued);
            repo.Add(queued);
            await repo.SaveChangesAsync(ct);

            var queuedDto = IniciarBiometriaHandler.ToDto(queued, queuedAt);
            return (new IniciarKyverumVerifyResult(queuedDto, string.Empty, Queued: true), null);
        }

        var now = DateTimeOffset.UtcNow;
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = validationId,
            TenantId = tenantId,
            ProcedureInstanceId = id,
            PartyRole = parte,
            Name = nombre,
            DocumentType = tipoDoc,
            DocumentNumber = documento,
            Email = email,
            Status = BiometricEstados.EnProceso,
            // Sin magic-link en Kyverum: token_hash aleatorio para cumplir NOT NULL/único.
            TokenHash = BiometricToken.Hash(BiometricToken.Generate()),
            ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras),
            Attempts = 0,
            MaxAttempts = BiometricRules.KyverumMaxIntentos,
            CreatedAt = now,
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = provider.VerificationId,
            CaptureUrl = provider.CaptureUrl,
            // Secreto del webhook (por-tenant, dashboard). Si no está configurado se deja null y el
            // webhook fallará cerrado (401) hasta configurarlo.
            WebhookSecretEncrypted = string.IsNullOrEmpty(provider.WebhookSecret)
                ? null
                : secretProtector.Protect(provider.WebhookSecret),
            ProviderStatus = provider.ProviderStatus,
            ProviderPayload = provider.RawPayloadSanitized,
        };

        instance.BiometricValidations.Add(validation);
        repo.Add(validation);

        await events.PublishAsync(new IdentityValidationRequested
        {
            TenantId = tenantId,
            ProcedureInstanceId = id,
            ValidationId = validation.Id,
            Provider = BiometricProviders.Kyverum,
            Parte = parte,
            ProviderVerificationId = provider.VerificationId,
        }, ct);

        await repo.SaveChangesAsync(ct);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.SendResponse, IdentityValidationAuditOutcomes.Ok,
            TenantId: tenantId, ProcedureInstanceId: id, ValidationId: validation.Id,
            KyverumVerificationId: provider.VerificationId, PartyRole: parte,
            SecretPresent: validation.WebhookSecretEncrypted is not null, ProviderStatus: provider.ProviderStatus,
            Message: "Create OK: validación en_proceso, captura enviada."), ct);

        var dto = IniciarBiometriaHandler.ToDto(validation, now);
        return (new IniciarKyverumVerifyResult(dto, provider.CaptureUrl), null);
    }

    // Parte vacía → comprador (matrícula, única parte), igual que SimularBiometriaHandler: así la
    // validación queda con Parte="comprador" y el gate del wizard (BiometriaAprobada) la reconoce.
    private static string? NormalizeParte(string? parte)
    {
        var p = string.IsNullOrWhiteSpace(parte)
            ? BiometricRules.ParteComprador
            : parte.Trim().ToLowerInvariant();
        return p is BiometricRules.ParteComprador or BiometricRules.ParteVendedor ? p : "invalid";
    }

    /// <summary>Primer valor no vacío (recortado), o null si ambos están vacíos.</summary>
    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim()
        : !string.IsNullOrWhiteSpace(b) ? b!.Trim()
        : null;
}

// ── Handler: webhook Kyverum (público) — AC2/AC3 ──────────────────────────────

/// <summary>
/// Procesa el webhook de Kyverum (HU #10233, AC2/AC3). Resuelve la validación por NUESTRO id (que viaja
/// en la URL del callback — el cuerpo no lo repite) y verifica la firma HMAC-SHA256
/// (<c>x-kv-signature: sha256=&lt;hex&gt;</c>) sobre el cuerpo CRUDO. Con firma válida confía en el cuerpo y
/// aplica el resultado. Firma presente y NO válida ⇒ <c>firma_invalida</c> (401) sin tocar la BD (AC3).
/// <para><b>Robustez (keyring):</b> si el secreto NO se puede descifrar (<see cref="CryptographicException"/>
/// del keyring de Data Protection) o está ausente, NUNCA devuelve 500 ni confía en el cuerpo: consulta el
/// estado real a Kyverum (autenticado con API key) y aplica ese resultado. Así el webhook SIGUE funcionando
/// y auto-cura la validación aunque el keyring esté roto entre réplicas/reinicios.</para>
/// Idempotente: estados terminales devuelven <c>ok</c> sin re-aplicar. Persiste provider_status/payload
/// SANITIZADO (sin OCR/PII) y emite <see cref="IdentityValidationCompleted"/> (outbox).
/// </summary>
public sealed class KyverumWebhookHandler(
    IProcedureInstanceRepository repo,
    IWebhookSecretProtector secretProtector,
    IKyverumVerifyClient kyverum,
    IdentityValidationResultApplier applier,
    IIdentityValidationAuditLog audit,
    ILogger<KyverumWebhookHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(string? Result, string? Error)> HandleAsync(KyverumWebhookInput input, CancellationToken ct = default)
    {
        var signaturePresent = !string.IsNullOrWhiteSpace(input.Signature);

        if (input.RawBody is null || input.RawBody.Length == 0)
        {
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.WebhookReceived, "cuerpo_invalido",
                ValidationId: input.ValidationId, SignaturePresent: signaturePresent, HttpStatus: 400), ct);
            return (null, "cuerpo_invalido");
        }

        // Correlación por NUESTRO id (de la URL). No se confía en el cuerpo para localizar la validación.
        var v = await repo.GetBiometricByIdAsync(input.ValidationId, ct);
        if (v is null)
        {
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.WebhookReceived, IdentityValidationAuditOutcomes.NotFound,
                ValidationId: input.ValidationId, SignaturePresent: signaturePresent, HttpStatus: 404), ct);
            return (null, "not_found");
        }

        var secretPresent = !string.IsNullOrWhiteSpace(v.WebhookSecretEncrypted);

        // LLEGADA del webhook: queda registrada SIEMPRE (con firma/secreto presentes).
        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.WebhookReceived, IdentityValidationAuditOutcomes.Received,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
            KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
            SignaturePresent: signaturePresent, SecretPresent: secretPresent), ct);

        // Descifrado del secreto (vía rápida HMAC). CryptographicException (keyring/ApplicationName) ⇒ NO 500.
        string? secret = null;
        string? decryptError = null;
        if (secretPresent)
        {
            try
            {
                secret = secretProtector.Unprotect(v.WebhookSecretEncrypted!);
            }
            catch (CryptographicException ex)
            {
                decryptError = ex.GetType().Name;
            }
        }

        if (secret is null)
        {
            // No verificable (secreto ausente o indescifrable) → auditar el diagnóstico y reconciliar por consulta.
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.WebhookNotVerifiable,
                secretPresent ? IdentityValidationAuditOutcomes.DecryptFailed : IdentityValidationAuditOutcomes.SecretMissing,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                SignaturePresent: signaturePresent, SecretPresent: secretPresent,
                DecryptOk: secretPresent ? false : null, ErrorType: decryptError,
                Message: secretPresent
                    ? "El secreto no se pudo descifrar (keyring/ApplicationName no coincide); se reconcilia por consulta."
                    : "La validación no tiene secreto de webhook; se reconcilia por consulta."), ct);
            WebhookLog.SecretNotVerifiable(logger, v.Id);
            return await ReconcileFromKyverumAsync(v, ct);
        }

        // Firma presente y verificable: si NO valida ⇒ 401 (seguridad, AC3).
        if (!KyverumWebhookVerifier.IsValid(input.RawBody, input.Signature, secret))
        {
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.WebhookSignatureInvalid, IdentityValidationAuditOutcomes.SignatureInvalid,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                SignaturePresent: signaturePresent, SecretPresent: true, DecryptOk: true, HttpStatus: 401), ct);
            return (null, "firma_invalida");
        }

        // Idempotencia: estados terminales no se re-procesan (AC2).
        if (v.Status is BiometricEstados.Aprobado or BiometricEstados.Rechazado)
            return ("ok", null);

        return await ApplyFromBodyAsync(v, input.RawBody, ct);
    }

    /// <summary>Vía rápida: firma válida ⇒ confiar en el cuerpo del webhook y aplicar.</summary>
    private async Task<(string? Result, string? Error)> ApplyFromBodyAsync(
        ProcedureInstanceBiometricValidation v, byte[] rawBody, CancellationToken ct)
    {
        KyverumWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<KyverumWebhookPayload>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            return (null, "cuerpo_invalido");
        }

        if (payload?.Data is null)
            return (null, "cuerpo_invalido");

        // Kyverum emite `validation.rejected` en CADA intento fallido aunque queden reintentos: un rechazo del
        // webhook NO es terminal por sí solo. El CONTEO de intentos es autoritativo aquí (un webhook = un intento),
        // deduplicando por una clave estable del cuerpo inmutable para que un redelivery del MISMO evento no
        // recuente; el worker/poll de reconciliación ya NO cuenta (así se elimina el doble-conteo webhook+poll que
        // inflaba los intentos). Con intentos aún disponibles la validación queda `en_proceso` (el cliente reintenta
        // en el móvil); solo se marca rechazado al agotar los intentos.
        if (!payload.Data.Aprobado)
        {
            // Clave del intento tomada del cuerpo INMUTABLE (closedAt del intento ?? ts del evento ?? requestId):
            // estable entre redeliveries del mismo evento y distinta entre intentos. Se recorta a la longitud
            // de la columna. `attempt_key` se registra en la bitácora (Detail) para poder auditar el dedup.
            var attemptKey = Truncate(FirstNonEmpty(payload.Data.ClosedAt, payload.Ts, payload.RequestId), 40);

            if (attemptKey is null)
            {
                // Cuerpo sin ninguna clave para deduplicar (caso patológico): NO se cuenta por webhook, para no
                // arriesgar un doble-conteo no deduplicable. Se reconcilia (sin contar) y el worker acompaña.
                await audit.LogAsync(new IdentityValidationAuditEntry(
                    IdentityValidationAuditStages.WebhookReceived, IdentityValidationAuditOutcomes.Pending,
                    TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                    KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                    SecretPresent: true, DecryptOk: true, ProviderStatus: payload.Evento, HttpStatus: 200,
                    Message: "Webhook de intento rechazado sin clave en el cuerpo: no se cuenta (se reconcilia).",
                    Detail: "attempt_key=<null>"), ct);
                return await ReconcileFromKyverumAsync(v, ct);
            }

            // Conteo ATÓMICO e idempotente: un único UPDATE con guarda `last_attempt_at <> @key`. Dos entregas
            // paralelas del MISMO intento cuentan una sola vez (la fila se bloquea y la 2ª no cumple la guarda);
            // esto blinda la ventana de doble-entrega del webhook que se observó en DEV.
            var counted = await repo.TryCountKyverumAttemptAsync(v.Id, attemptKey, DateTimeOffset.UtcNow, ct);
            if (!counted)
            {
                // Redelivery del mismo intento (o la validación ya no está en_proceso): no se recuenta.
                await audit.LogAsync(new IdentityValidationAuditEntry(
                    IdentityValidationAuditStages.WebhookReceived, IdentityValidationAuditOutcomes.Pending,
                    TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                    KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                    SecretPresent: true, DecryptOk: true, ProviderStatus: payload.Evento, HttpStatus: 200,
                    Message: "Webhook de intento rechazado ya contado (redelivery): no se recuenta.",
                    Detail: $"attempt_key={attemptKey}"), ct);
                return ("ok", null);
            }

            // El UPDATE atómico ya persistió el conteo + reinició el presupuesto de sondeo del worker. Se recarga
            // la entidad rastreada para ver el nuevo conteo antes de enriquecer/terminalizar.
            await repo.ReloadBiometricAsync(v, ct);

            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.WebhookReceived, IdentityValidationAuditOutcomes.Pending,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                SecretPresent: true, DecryptOk: true, ProviderStatus: payload.Evento, HttpStatus: 200,
                Message: $"Webhook de intento rechazado contado ({v.Attempts}/{v.MaxAttempts}). Se reconcilia contra el resultado.",
                Detail: $"attempt_key={attemptKey}; conteo={v.Attempts}/{v.MaxAttempts}"), ct);

            // Enriquece el motivo del último intento y, si el conteo YA agotó los intentos, terminaliza en
            // rechazado. ApplyStatusAsync (vía ReconcileFromKyverumAsync) ya NO cuenta: decide con v.Attempts.
            return await ReconcileFromKyverumAsync(v, ct);
        }

        var subject = SelectSubject(payload.Data.Subjects, v.PartyRole);
        await applier.ApplyAsync(
            v,
            new IdentityValidationTerminalResult(
                true, payload.Evento, Sanitize(payload, subject), subject?.Score),
            DateTimeOffset.UtcNow,
            ct);

        await repo.SaveChangesAsync(ct);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.WebhookApplied, v.Status,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
            KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
            SecretPresent: true, DecryptOk: true, ProviderStatus: payload.Evento, HttpStatus: 200,
            Message: "Resultado aplicado desde el webhook (firma válida, aprobado)."), ct);
        return ("ok", null);
    }

    /// <summary>
    /// Respaldo cuando no se puede verificar la firma: consulta el estado real a Kyverum (autenticado con el
    /// API key, no depende del keyring) y lo aplica. Devuelve <c>reintentar</c> ante fallo transitorio del
    /// proveedor (→ 503, Kyverum reintenta el webhook).
    /// </summary>
    private async Task<(string? Result, string? Error)> ReconcileFromKyverumAsync(
        ProcedureInstanceBiometricValidation v, CancellationToken ct)
    {
        if (v.Status is BiometricEstados.Aprobado or BiometricEstados.Rechazado)
            return ("ok", null);
        if (string.IsNullOrWhiteSpace(v.KyverumVerificationId))
            return ("ok", null); // sin id no hay cómo consultar; el worker/reintento lo cubrirá.

        KyverumVerifyStatus? status;
        try
        {
            status = await kyverum.GetStatusAsync(v.KyverumVerificationId, v.PartyRole, ct);
        }
        catch (KyverumVerifyException ex)
        {
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.Reconcile, IdentityValidationAuditOutcomes.Error,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                ErrorType: nameof(KyverumVerifyException), HttpStatus: ex.Transient ? 503 : 502,
                Message: "Falló la consulta de estado durante el respaldo del webhook."), ct);
            return (null, ex.Transient ? "reintentar" : "no_verificable");
        }

        if (status is null)
            return ("ok", null);

        // El reconciliador cuenta los intentos (rechazado_intento) y persiste conteo/motivo o terminaliza al
        // agotar; devuelve si cambió algo. Se guarda cuando cambió (aprobado/rechazado/en_proceso con intento).
        var applied = await IdentityValidationReconciler.ApplyStatusAsync(applier, v, status, DateTimeOffset.UtcNow, ct);
        if (applied)
            await repo.SaveChangesAsync(ct);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.Reconcile,
            applied ? v.Status : IdentityValidationAuditOutcomes.Pending,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
            KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
            ProviderStatus: status.Status,
            Message: applied
                ? $"Respaldo del webhook: sincronizado (estado: {v.Status}; intentos {v.Attempts}/{v.MaxAttempts})."
                : "Respaldo del webhook: Kyverum aún no resuelve (sigue en proceso).",
            Detail: $"validado_at={status.AttemptAt ?? "<null>"}"), ct);
        return ("ok", null);
    }

    /// <summary>Primer valor no vacío (recortado), o null si todos están vacíos.</summary>
    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        return null;
    }

    /// <summary>Recorta a <paramref name="max"/> caracteres (para respetar el largo de la columna). Null-safe.</summary>
    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>Sujeto de la parte (match por rol, case-insensitive); si no, el primero del arreglo.</summary>
    private static KyverumWebhookSubject? SelectSubject(IReadOnlyList<KyverumWebhookSubject>? subjects, string? parte)
    {
        if (subjects is not { Count: > 0 })
            return null;
        if (!string.IsNullOrWhiteSpace(parte))
        {
            foreach (var s in subjects)
                if (string.Equals(s.Rol, parte, StringComparison.OrdinalIgnoreCase))
                    return s;
        }
        return subjects[0];
    }

    /// <summary>
    /// Payload sanitizado para trazabilidad: ids del evento, veredicto, score, sello de firma y
    /// coincidencias (booleans). NUNCA incluye <c>datosExtraidos</c> (OCR: nombres/fecha/sexo = PII cruda).
    /// </summary>
    private static string Sanitize(KyverumWebhookPayload payload, KyverumWebhookSubject? subject) =>
        JsonSerializer.Serialize(new
        {
            evento = payload.Evento,
            request_id = payload.RequestId,
            delivery_id = payload.DeliveryId,
            aprobado = payload.Data?.Aprobado,
            status = subject?.Status,
            score = subject?.Score,
            firma_serie = subject?.FirmaSerie,
            coincidencias = subject?.Coincidencias,
            proveedor = BiometricProviders.Kyverum,
        });
}

// ── Contrato del webhook Kyverum (CONTRATO-API.md). datosExtraidos se omite a propósito (PII OCR). ──
public sealed record KyverumWebhookPayload(
    [property: JsonPropertyName("evento")] string? Evento,
    [property: JsonPropertyName("requestId")] string? RequestId,
    [property: JsonPropertyName("data")] KyverumWebhookData? Data,
    [property: JsonPropertyName("deliveryId")] string? DeliveryId,
    [property: JsonPropertyName("ts")] string? Ts);

public sealed record KyverumWebhookData(
    [property: JsonPropertyName("aprobado")] bool Aprobado,
    [property: JsonPropertyName("closedAt")] string? ClosedAt,
    [property: JsonPropertyName("subjects")] IReadOnlyList<KyverumWebhookSubject>? Subjects);

public sealed record KyverumWebhookSubject(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("rol")] string? Rol,
    [property: JsonPropertyName("documento")] string? Documento,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("score")] int? Score,
    [property: JsonPropertyName("firmaSerie")] string? FirmaSerie,
    [property: JsonPropertyName("coincidencias")] KyverumCoincidencias? Coincidencias);

public sealed record KyverumCoincidencias(
    [property: JsonPropertyName("documento")] bool Documento,
    [property: JsonPropertyName("nombre")] bool Nombre);

/// <summary>Logging source-generated (CA1848) del webhook. NUNCA incluye el secreto ni PII.</summary>
internal static partial class WebhookLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Webhook Kyverum {ValidationId}: firma no verificable (secreto ausente o keyring no puede descifrar); se reconcilia por consulta autenticada.")]
    public static partial void SecretNotVerifiable(ILogger logger, Guid validationId);
}
